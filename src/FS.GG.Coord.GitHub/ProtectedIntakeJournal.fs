namespace FS.GG.Coord.GitHub

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coordination.GitHub

module ProtectedIntakeJournal =
    type Read =
        { Repository: string
          RepositoryId: int64
          Ref: string
          Protection: ProtectionObservation
          FirstHead: string option
          SecondHead: string option
          Observation: JournalObservation
          CommitBytes: Map<string, byte[]>
          TreeBytes: Map<string, byte[]> }

    type Objects =
        { EventObjectId: string; EventBytes: byte[]
          HeadObjectId: string; HeadBytes: byte[]
          TreeObjectId: string; TreeBytes: byte[]
          CommitObjectId: string; CommitBytes: byte[] }

    type Proposal = { Cas: CasProposal; Objects: Objects }
    type CompareAndSwapResult = Won | ParentConflict | Refused of string | ResponseUnknown
    type Port =
        { Read: AggregateAddress -> Read
          CompareAndSwap: Proposal -> CompareAndSwapResult }
    type Snapshot =
        { State: IntakeTransaction.State
          Head: JournalSnapshot }
    type AppendResult =
        | Appended of Snapshot
        | Conflict of Snapshot option
        | DefiniteRefusal of string
        | Indeterminate of string
    type StrongAbsence =
        { OriginalRequestSha256: string
          EvidenceDigest: string
          Kind: string }
    type ProviderReconciliation =
        { Read: IntakeTransaction.Identity -> byte[] -> Result<StrongAbsence option, string> }

    let address =
        ShardedJournalAdapter.address Operation IntakeTransaction.AggregateId
        |> Result.defaultWith (string >> invalidOp)

    do
        if address.Ref <> IntakeTransaction.JournalRef then
            invalidOp "retained intake aggregate resolved to an unexpected protected ref"

    let private gitOid kind (bytes: byte[]) =
        let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
        SHA1.HashData(Array.append header bytes)
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    // ShardedJournalAdapter requires sorted, newline-terminated canonical JSON. The Core event is
    // separately canonical and bounded; a single base64 field preserves its exact bytes inside that blob.
    let private wrapEvent (eventBytes: byte[]) =
        JsonSerializer.Serialize({| intakeEventBase64 = Convert.ToBase64String(eventBytes) |})
        |> ShardedJournalAdapter.canonicalJson
        |> Result.defaultWith invalidOp

    let private unwrapEvent (blob: byte[]) =
        try
            use document = JsonDocument.Parse(blob)
            let root = document.RootElement
            if root.ValueKind <> JsonValueKind.Object then Error "intake-event-blob-shape"
            else
                let encoded = root.GetProperty("intakeEventBase64").GetString()
                let eventBytes = Convert.FromBase64String(encoded)
                if eventBytes.Length > IntakeTransaction.MaxEventBytes || wrapEvent eventBytes <> blob then
                    Error "intake-event-blob-noncanonical"
                else
                    IntakeTransaction.decodeEvent eventBytes
        with :? JsonException | :? FormatException | :? InvalidOperationException | :? KeyNotFoundException ->
            Error "intake-event-blob-malformed"

    let private treeBytes (eventOid: string) (headOid: string) =
        use stream = new MemoryStream()
        for name, oid in [ "event.json", eventOid; "head.json", headOid ] do
            let header = Encoding.UTF8.GetBytes("100644 " + name + "\u0000")
            stream.Write(header, 0, header.Length)
            let raw = Convert.FromHexString(oid)
            stream.Write(raw, 0, raw.Length)
        stream.ToArray()

    let private rawObjectsValid (read: Read) (commit: JournalCommit) =
        match Map.tryFind commit.CommitOid read.CommitBytes, Map.tryFind commit.TreeOid read.TreeBytes with
        | Some commitBytes, Some tree ->
            let lines = Encoding.UTF8.GetString(commitBytes).Split('\n')
            let parentLine = commit.ParentOid |> Option.map (fun parent -> "parent " + parent)
            let headerValid =
                lines.Length >= 4
                && lines[0] = "tree " + commit.TreeOid
                && (match parentLine with
                    | Some expected -> lines[1] = expected
                    | None -> lines[1].StartsWith("author ", StringComparison.Ordinal))
            gitOid "commit" commitBytes = commit.CommitOid
            && gitOid "tree" tree = commit.TreeOid
            && tree = treeBytes (gitOid "blob" commit.Event.Bytes) (gitOid "blob" commit.HeadBytes)
            && headerValid
        | _ -> false

    let restore (read: Read) =
        if
            read.Repository <> "FS-GG/FS.GG.Coordination.Authority"
            || read.RepositoryId <> 1351660651L
            || read.Ref <> address.Ref
            || read.FirstHead <> read.SecondHead
            || read.FirstHead.IsNone
        then
            Error "intake-journal-head-unconfirmed"
        else
            ShardedJournalAdapter.validateProtection read.Protection
            |> Result.mapError (fun failure -> "intake-journal-protection:" + string failure)
            |> Result.bind (fun () ->
                ShardedJournalAdapter.validate address read.Observation
                |> Result.mapError (fun failure -> "intake-journal:" + string failure)
                |> Result.bind (fun head ->
                    if read.FirstHead <> Some head.Current.CommitOid then
                        Error "intake-journal-observation-head-mismatch"
                    elif head.Commits |> List.exists (rawObjectsValid read >> not) then
                        Error "intake-journal-raw-objects-invalid"
                    else
                        let folder prior commit =
                            prior
                            |> Result.bind (fun state ->
                                unwrapEvent commit.Event.Bytes
                                |> Result.bind (IntakeTransaction.apply state)
                                |> Result.map Some)
                        head.Commits
                        |> List.fold folder (Ok None)
                        |> Result.bind (function
                            | Some state -> Ok { State = state; Head = head }
                            | None -> Error "intake-journal-empty")))

    let private commitBytes treeOid parentOid operationId =
        Encoding.UTF8.GetBytes(
            String.concat "\n"
                [ "tree " + treeOid
                  "parent " + parentOid
                  "author FS.GG Coordination <coordination@fs.gg> 0 +0000"
                  "committer FS.GG Coordination <coordination@fs.gg> 0 +0000"
                  ""
                  "fsgg intake " + operationId
                  "" ])

    let planAppend operationId (read: Read) event =
        if String.IsNullOrWhiteSpace operationId || operationId.Contains('\n') || operationId.Contains('\r') then
            Error "intake-operation-id-invalid"
        else
            restore read
            |> Result.bind (fun current ->
                IntakeTransaction.apply (Some current.State) event
                |> Result.bind (fun _ -> IntakeTransaction.encodeEvent event)
                |> Result.bind (fun eventBytes ->
                    let blob = wrapEvent eventBytes
                    let eventDigest = ShardedJournalAdapter.sha256 blob
                    let eventOid = gitOid "blob" blob
                    let prior = current.Head.Current
                    let provisional =
                        { SchemaVersion = 1
                          Address = address
                          Generation = prior.Head.Generation + 1L
                          EventDigest = eventDigest
                          SnapshotDigest = None
                          Terminal = (match event with IntakeTransaction.Bound _ -> true | _ -> false)
                          PriorHeadDigest = Some prior.Head.HeadDigest
                          HeadDigest = String.replicate 64 "0" }
                    let head =
                        { provisional with
                            HeadDigest =
                                ShardedJournalAdapter.journalHeadBytes provisional
                                |> ShardedJournalAdapter.sha256 }
                    let headBytes = ShardedJournalAdapter.journalHeadBytes head
                    let headOid = gitOid "blob" headBytes
                    let tree = treeBytes eventOid headOid
                    let treeOid = gitOid "tree" tree
                    let commit = commitBytes treeOid prior.CommitOid operationId
                    let commitOid = gitOid "commit" commit
                    let proposed: JournalCommit =
                        { CommitOid = commitOid
                          ParentOid = Some prior.CommitOid
                          TreeOid = treeOid
                          OperationId = operationId
                          Head = head
                          HeadBytes = headBytes
                          Event = { Bytes = blob; Digest = eventDigest }
                          Checkpoint = None }
                    ShardedJournalAdapter.planCas operationId current.Head proposed
                    |> Result.mapError (fun failure -> "intake-cas:" + string failure)
                    |> Result.map (fun cas ->
                        { Cas = cas
                          Objects =
                            { EventObjectId = eventOid; EventBytes = blob
                              HeadObjectId = headOid; HeadBytes = headBytes
                              TreeObjectId = treeOid; TreeBytes = tree
                              CommitObjectId = commitOid; CommitBytes = commit } })))

    let private proposalObserved (proposal: Proposal) (snapshot: Snapshot) =
        snapshot.Head.Commits
        |> List.exists (fun commit ->
            commit.CommitOid = proposal.Cas.ProposedCommit.CommitOid
            && commit.TreeOid = proposal.Cas.ProposedCommit.TreeOid
            && commit.OperationId = proposal.Cas.OperationId
            && commit.Head.HeadDigest = proposal.Cas.ProposedCommit.Head.HeadDigest
            && commit.Event.Bytes = proposal.Cas.ProposedCommit.Event.Bytes)

    let append (port: Port) operationId event =
        let before = port.Read address
        planAppend operationId before event
        |> Result.map (fun proposal ->
            // The port must send an atomic expected-head CAS to the protected ref. A second stable read
            // narrows stale work, but the CAS itself is the deciding write boundary.
            let immediate = port.Read address
            let outcome =
                if immediate.FirstHead = before.FirstHead && Result.isOk (restore immediate) then
                    port.CompareAndSwap proposal
                else
                    ParentConflict
            let after = port.Read address
            let restored = restore after
            match restored with
            | Ok snapshot when proposalObserved proposal snapshot -> Appended snapshot
            | _ ->
                match outcome with
                | ParentConflict -> Conflict(Result.toOption restored)
                | Refused reason -> DefiniteRefusal reason
                | Won -> Indeterminate "accepted-intake-commit-not-observed"
                | ResponseUnknown -> Indeterminate "intake-append-response-unknown")

    let private validDigest (value: string) =
        not (isNull value)
        && value.Length = 64
        && value |> Seq.forall (fun c -> c >= '0' && c <= '9' || c >= 'a' && c <= 'f')

    let originalRequestAfterStrongAbsence (snapshot: Snapshot) (provider: ProviderReconciliation) =
        let binding = snapshot.State.Binding
        if snapshot.State.Phase <> "InFlight" then
            Error "intake-is-not-in-flight"
        elif ShardedJournalAdapter.sha256 binding.RequestBytes <> binding.RequestSha256 then
            Error "retained-original-request-digest-mismatch"
        else
            provider.Read binding.Identity (Array.copy binding.RequestBytes)
            |> Result.bind (function
                | Some proof when
                    validDigest proof.EvidenceDigest
                    && proof.OriginalRequestSha256 = binding.RequestSha256
                    && List.contains proof.Kind
                        [ "idempotency-key-excluded"; "conditional-fence-excluded"; "original-request-retired" ]
                    ->
                    Ok(Array.copy binding.RequestBytes)
                | _ -> Error "strong-absence-proof-does-not-bind-original-request")
