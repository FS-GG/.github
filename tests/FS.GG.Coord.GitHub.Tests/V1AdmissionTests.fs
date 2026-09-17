module FS.GG.Coord.GitHub.Tests.V1AdmissionTests

open System
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub

module Registry = V1AdmissionRegistry

let private digest c =
    String.replicate 64 c |> Registry.sha256Digest |> Result.defaultWith failwith

let private oid c =
    String.replicate 40 c |> Registry.gitObjectId |> Result.defaultWith failwith

let private bytesDigest (bytes: byte array) =
    SHA256.HashData bytes
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> Registry.sha256Digest
    |> Result.defaultWith failwith

let private gitOid kind (bytes: byte array) =
    let header = Encoding.UTF8.GetBytes($"{kind} {bytes.Length}\u0000")

    SHA1.HashData(Array.append header bytes)
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> Registry.gitObjectId
    |> Result.defaultWith failwith

let private treeBytes entries =
    entries
    |> List.sortBy fst
    |> List.collect (fun (name, value) ->
        (Encoding.UTF8.GetBytes($"100644 {name}\u0000") |> Array.toList)
        @ (Registry.gitObjectIdValue value |> Convert.FromHexString |> Array.toList))
    |> List.toArray

let private authorityObjects () =
    let manifest, trust = digest "a", digest "b"

    let eventBytes =
        Encoding.UTF8.GetBytes(
            $"{{\"fleetId\":\"fs-gg-production\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"phase\":\"OperatingV1\",\"schema\":\"fsgg.github-substrate.epoch-event/1\",\"trustAnchorSha256\":\"{Registry.sha256Value trust}\"}}"
        )

    let eventOid = gitOid "blob" eventBytes

    let address =
        ShardedJournalAdapter.address Cutover "fleet-cutover:fs-gg-production"
        |> Result.defaultWith (string >> failwith)

    let headBytes =
        ShardedJournalAdapter.journalHeadBytes
            {
                SchemaVersion = 1
                Address = address
                Generation = 1L
                EventDigest = ShardedJournalAdapter.sha256 eventBytes
                SnapshotDigest = None
                Terminal = false
                PriorHeadDigest = None
                HeadDigest = ""
            }

    let headOid = gitOid "blob" headBytes
    let entries = Map.ofList [ "event.json", eventOid; "head.json", headOid ]
    let tree = treeBytes (Map.toList entries)
    let treeOid = gitOid "tree" tree

    let commitBytes =
        Encoding.UTF8.GetBytes(
            $"tree {Registry.gitObjectIdValue treeOid}\nauthor test <test@fs.gg> 0 +0000\ncommitter test <test@fs.gg> 0 +0000\n\nfixture\n"
        )

    let commit = gitOid "commit" commitBytes

    let observed =
        {
            Repository = "FS-GG/FS.GG.Coordination.Authority"
            RepositoryId = 1351660651L
            Ref = "refs/heads/fsgg/v2/journal/cutover/d5"
            FirstHead = commit
            TagTarget = commit
            Commit = commit
            Parent = None
            GenesisCommit = commit
            Ancestry = [ commit ]
            CommitTree = treeOid
            CommitBytes = commitBytes
            TreeBytes = tree
            TreeEntries = entries
            EventBlob = eventOid, eventBytes
            HeadBlob = headOid, headBytes
            TrustAnchorSha256 = trust
            ManifestSha256 = manifest
            ClaimJournals = Map.empty
        }

    let port: AuthorityGitPort =
        {
            ReadObjects = fun () -> Ok observed
            RereadHead = fun () -> Ok observed.FirstHead
        }

    let snapshot =
        Registry.readVerified port |> Result.defaultWith (String.concat "," >> failwith)

    snapshot, commit, manifest, observed

let private genesisRead manifest =
    let eventBytes =
        ShardedJournalAdapter.canonicalJson (
            $"{{\"commandId\":\"fixture-initialize\",\"kind\":\"initialize\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"payload\":{{}},\"round\":1,\"schema\":\"fsgg.github-substrate.admission-event/1\"}}"
        )
        |> Result.defaultWith failwith

    let eventDigest = ShardedJournalAdapter.sha256 eventBytes

    let provisional =
        {
            SchemaVersion = 1
            Address = registryAddress
            Generation = 1L
            EventDigest = eventDigest
            SnapshotDigest = None
            Terminal = false
            PriorHeadDigest = None
            HeadDigest = String.replicate 64 "0"
        }

    let head =
        { provisional with
            HeadDigest =
                ShardedJournalAdapter.journalHeadBytes provisional
                |> ShardedJournalAdapter.sha256
        }

    let headBytes = ShardedJournalAdapter.journalHeadBytes head
    let eventOid, headOid = gitOid "blob" eventBytes, gitOid "blob" headBytes
    let tree = treeBytes [ "event.json", eventOid; "head.json", headOid ]
    let treeOid = gitOid "tree" tree

    let commitBytes =
        Encoding.UTF8.GetBytes(
            $"tree {Registry.gitObjectIdValue treeOid}\nauthor FS.GG Coordination <coordination@fs.gg> 0 +0000\ncommitter FS.GG Coordination <coordination@fs.gg> 0 +0000\n\nfsgg admission fixture-initialize\n"
        )

    let commitOid = gitOid "commit" commitBytes

    let commit =
        {
            CommitOid = Registry.gitObjectIdValue commitOid
            ParentOid = None
            TreeOid = Registry.gitObjectIdValue treeOid
            OperationId = "fixture-initialize"
            Head = head
            HeadBytes = headBytes
            Event =
                {
                    Bytes = eventBytes
                    Digest = eventDigest
                }
            Checkpoint = None
        }

    {
        Repository = "FS-GG/FS.GG.Coordination.Authority"
        RepositoryId = 1351660651L
        Ref = registryAddress.Ref
        FirstHead = Some commitOid
        SecondHead = Some commitOid
        Observation = JournalComplete("fixture-genesis", [ commit ])
        CommitBytes = Map.ofList [ commit.CommitOid, commitBytes ]
        TreeBytes = Map.ofList [ commit.TreeOid, tree ]
    }

let private commitsOf read =
    match read.Observation with
    | JournalComplete(_, commits) -> commits
    | _ -> failwith "invalid fixture journal"

let private appendRead read proposal =
    let cas, objects = Registry.proposalCas proposal, Registry.proposalObjects proposal

    { read with
        FirstHead = Some objects.CommitObjectId
        SecondHead = Some objects.CommitObjectId
        Observation = JournalComplete("test-revision", commitsOf read @ [ cas.ProposedCommit ])
        CommitBytes = Map.add (Registry.gitObjectIdValue objects.CommitObjectId) objects.CommitBytes read.CommitBytes
        TreeBytes = Map.add (Registry.gitObjectIdValue objects.TreeObjectId) objects.TreeBytes read.TreeBytes
    }

let private context commit manifest =
    {
        Round = 1L
        Manifest = manifest
        OperationId = "op-1"
        OperationGeneration = 1L
        Actor = "worker-a"
        Receiver = "coordination"
        Kind = "issue-edit"
        CanonicalTarget = "FS-GG/example#17"
        Claim = NoClaimRequired
        IntentDigest = digest "d"
        TouchSetDigest = digest "e"
        OriginatingEpochCommit = commit
        OriginatingEpochGeneration = 1L
    }

type private RuntimeFixture(?outcomes: JournalCompareAndSwapOutcome list) =
    let snapshot, commit, manifest, objects = authorityObjects ()
    let mutable current = genesisRead manifest
    let mutable pending = defaultArg outcomes []
    let mutable readCount = 0

    let mutable readTransform: int -> RegistryJournalRead -> RegistryJournalRead =
        fun _ read -> read

    let mutable rereadHead: unit -> Result<GitObjectId, string> =
        fun () -> Ok objects.FirstHead

    do
        let registry =
            Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

        let candidate =
            match Registry.admit (Registry.head registry) snapshot (context commit manifest) registry with
            | RegistryAdmissionAppended value -> value
            | other -> failwithf "admission failed: %A" other

        let proposal =
            Registry.planAppend "fixture-admit" current candidate
            |> Result.defaultWith (String.concat "," >> failwith)

        current <- appendRead current proposal

    member _.Current = current
    member _.AuthorityObjects = objects

    member _.SetReadTransform(value) =
        readCount <- 0
        readTransform <- value

    member _.SetRereadHead(value) = rereadHead <- value

    member _.Journal: DurableJournal =
        {
            Read =
                fun _ ->
                    readCount <- readCount + 1
                    readTransform readCount current
            CompareAndSwap =
                fun proposal ->
                    let outcome =
                        match pending with
                        | head :: tail ->
                            pending <- tail
                            head
                        | [] -> CompareAndSwapWon

                    match outcome with
                    | CompareAndSwapWon
                    | CompareAndSwapResponseUnknown -> current <- appendRead current proposal
                    | _ -> ()

                    outcome
        }

    member _.Authority: DurableAuthority =
        {
            ReadObjects = fun () -> Ok objects
            RereadHead = fun () -> rereadHead ()
        }

    member this.Fence(owner) =
        let scope =
            operationScope "op-1" owner 1L None
            |> Result.defaultWith (String.concat "," >> failwith)

        DurableMutationFence(this.Authority, this.Journal, scope) :> IMutationFence

let private responseBytes (value: string) = Encoding.UTF8.GetBytes value

let private appliedEvidence value =
    Applied(bytesDigest (responseBytes value))

let private restored (fixture: RuntimeFixture) =
    Registry.restore fixture.Current
    |> Result.defaultWith (String.concat "," >> failwith)

let private scopedEffectId = "operation:4:op-1:generation:1:effect:effect-1"

[<Fact>]
let ``journal adapter reports accepted only for an explicitly won CAS`` () =
    let translate outcome =
        let journal: DurableJournal =
            {
                Read = fun _ -> Unchecked.defaultof<RegistryJournalRead>
                CompareAndSwap = fun _ -> outcome
            }

        (journalPort journal).Write(Unchecked.defaultof<RegistryAppendProposal>)

    Assert.Equal(ReceiveAccepted, translate CompareAndSwapWon)
    Assert.Equal(ReceiveParentConflict, translate CompareAndSwapParentConflict)
    Assert.Equal(ReceiveDefiniteRefusal "denied", translate (CompareAndSwapRefused "denied"))
    Assert.Equal(ReceiveResponseUnknown, translate CompareAndSwapResponseUnknown)

[<Fact>]
let ``durable fence sends and returns success only after accepted intent and Applied settlement`` () =
    let fixture = RuntimeFixture([ CompareAndSwapWon; CompareAndSwapWon ])
    let mutable calls = 0

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.Equal(Ok(AppliedResponse "response"), result)
    Assert.Equal(1, calls)
    Assert.DoesNotContain(scopedEffectId, restored fixture |> Registry.unresolvedEffects)

[<Fact>]
let ``response-unknown intent never mints a provider send permit`` () =
    let fixture = RuntimeFixture([ CompareAndSwapResponseUnknown ])
    let mutable calls = 0

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.True(Result.isError result)
    Assert.Equal(0, calls)

[<Fact>]
let ``provider success is withheld when durable Applied settlement conflicts`` () =
    let fixture = RuntimeFixture([ CompareAndSwapWon; CompareAndSwapParentConflict ])
    let mutable calls = 0

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.True(Result.isError result)
    Assert.Equal(1, calls)

[<Fact>]
let ``initial provider Partial evidence is durably unresolved with its response preserved`` () =
    let fixture = RuntimeFixture()

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () -> Ok "partial-response"),
                (fun _ -> Partial "response-sha256=fixture")
            )

    Assert.Equal(Ok(UnresolvedResponse "partial-response"), result)
    Assert.Contains(scopedEffectId, restored fixture |> Registry.unresolvedEffects)

[<Fact>]
let ``fresh authority movement refuses before provider send`` () =
    let fixture = RuntimeFixture()
    let mutable rereads = 0

    fixture.SetRereadHead(fun () ->
        rereads <- rereads + 1

        if rereads = 1 then
            Ok fixture.AuthorityObjects.FirstHead
        else
            Ok(oid "f"))

    let mutable calls = 0

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.True(Result.isError result)
    Assert.Equal(0, calls)

[<Fact>]
let ``fresh journal movement refuses before provider send`` () =
    let fixture = RuntimeFixture()

    fixture.SetReadTransform(fun count read ->
        if count < 3 then
            read
        else
            { read with SecondHead = Some(oid "f") })

    let mutable calls = 0

    let result =
        fixture
            .Fence("worker-a")
            .Dispatch(
                "effect-1",
                Encoding.UTF8.GetBytes "request",
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.True(Result.isError result)
    Assert.Equal(0, calls)

[<Fact>]
let ``competing owner cannot take over an unresolved provider attempt`` () =
    let fixture = RuntimeFixture()
    let request = Encoding.UTF8.GetBytes "request"

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        fixture.Fence("worker-a").Dispatch("effect-1", request, (fun () -> Error "lost"), appliedEvidence)
    )

    let mutable calls = 0

    let competing =
        fixture
            .Fence("worker-b")
            .Dispatch(
                "effect-1",
                request,
                (fun () ->
                    calls <- calls + 1
                    Ok "response"),
                appliedEvidence
            )

    Assert.True(Result.isError competing)
    Assert.Equal(0, calls)

[<Fact>]
let ``provider lost response remains unresolved and can be reconciled`` () =
    let fixture = RuntimeFixture()
    let fence = fixture.Fence("worker-a")
    let request = Encoding.UTF8.GetBytes "request"

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        fence.Dispatch("effect-1", request, (fun () -> Error "lost"), appliedEvidence)
    )

    Assert.Contains(scopedEffectId, restored fixture |> Registry.unresolvedEffects)

    let provider: ProviderReconciliation =
        {
            Read = fun _ _ _ _ -> Ok(Applied(bytesDigest (responseBytes "recovered")))
        }

    Assert.Equal(Ok(), fence.Reconcile("effect-1", provider))
    Assert.DoesNotContain(scopedEffectId, restored fixture |> Registry.unresolvedEffects)

[<Fact>]
let ``ProvenAbsent retry reuses persisted bytes and obtains a fresh permit`` () =
    let fixture = RuntimeFixture()
    let fence = fixture.Fence("worker-a")
    let request = Encoding.UTF8.GetBytes "original-request"

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        fence.Dispatch("effect-1", request, (fun () -> Error "lost"), appliedEvidence)
    )

    let absence: ProviderReconciliation =
        {
            Read = fun _ _ _ observed -> Ok(StronglyAbsent(ConditionalFenceExcluded, bytesDigest observed, digest "9"))
        }

    Assert.Equal(Ok(), fence.Reconcile("effect-1", absence))
    request[0] <- byte 'X'
    let mutable retried = Array.empty<byte>

    let result =
        fence.RetryProvenAbsent(
            "effect-1",
            (fun persisted ->
                retried <- Array.copy persisted
                Ok "response"),
            appliedEvidence
        )

    Assert.Equal(Ok(AppliedResponse "response"), result)
    Assert.Equal<byte>(Encoding.UTF8.GetBytes "original-request", retried)

[<Fact>]
let ``retry provider Indeterminate evidence is durably unresolved with its response preserved`` () =
    let fixture = RuntimeFixture()
    let fence = fixture.Fence("worker-a")
    let request = Encoding.UTF8.GetBytes "original-request"

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        fence.Dispatch("effect-1", request, (fun () -> Error "lost"), appliedEvidence)
    )

    let absence: ProviderReconciliation =
        {
            Read = fun _ _ _ observed -> Ok(StronglyAbsent(ConditionalFenceExcluded, bytesDigest observed, digest "9"))
        }

    Assert.Equal(Ok(), fence.Reconcile("effect-1", absence))

    let result =
        fence.RetryProvenAbsent(
            "effect-1",
            (fun _ -> Ok "unknown-response"),
            (fun _ -> Indeterminate "response-sha256=fixture")
        )

    Assert.Equal(Ok(UnresolvedResponse "unknown-response"), result)
    Assert.Contains(scopedEffectId, restored fixture |> Registry.unresolvedEffects)

[<Theory>]
[<InlineData("partial")>]
[<InlineData("indeterminate")>]
let ``Partial and Indeterminate settlements never permit retry`` kind =
    let fixture = RuntimeFixture()
    let fence = fixture.Fence("worker-a")
    let request = Encoding.UTF8.GetBytes "request"

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        fence.Dispatch("effect-1", request, (fun () -> Error "lost"), appliedEvidence)
    )

    let provider: ProviderReconciliation =
        {
            Read =
                fun _ _ _ _ ->
                    if kind = "partial" then
                        Ok(Partial "pending")
                    else
                        Ok(Indeterminate "unknown")
        }

    Assert.Equal(Ok(), fence.Reconcile("effect-1", provider))
    let mutable calls = 0

    let result =
        fence.RetryProvenAbsent(
            "effect-1",
            (fun _ ->
                calls <- calls + 1
                Ok "response"),
            appliedEvidence
        )

    Assert.True(Result.isError result)
    Assert.Equal(0, calls)

[<Fact>]
let ``provider evidence for different bytes cannot exclude a delayed original request`` () =
    let originalBytes = Encoding.UTF8.GetBytes "the-original\n"
    let differentDigest = bytesDigest (Encoding.UTF8.GetBytes "some-other-request\n")
    let evidenceDigest = bytesDigest (Encoding.UTF8.GetBytes "provider-proof\n")

    let provider: ProviderReconciliation =
        {
            Read = fun _ _ _ _ -> Ok(StronglyAbsent(IdempotencyKeyExcluded, differentDigest, evidenceDigest))
        }

    match (providerPort provider).Read "operation" "effect" 1L originalBytes with
    | Error "strong-absence-does-not-bind-original-request" -> ()
    | other -> failwithf "mismatched absence evidence must refuse, got %A" other

[<Fact>]
let ``ordinary producer restore refuses an absent genesis journal`` () =
    let missing =
        {
            Repository = "FS-GG/FS.GG.Coordination.Authority"
            RepositoryId = 1351660651L
            Ref = registryAddress.Ref
            FirstHead = None
            SecondHead = None
            Observation = JournalDeleted
            CommitBytes = Map.empty
            TreeBytes = Map.empty
        }

    match Registry.restore missing with
    | Error reasons -> Assert.Contains("registry-journal-identity-or-head", reasons)
    | Ok _ -> failwith "an ordinary producer must not initialize registry genesis"
