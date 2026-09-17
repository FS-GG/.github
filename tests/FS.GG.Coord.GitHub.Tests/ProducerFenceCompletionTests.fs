module FS.GG.Coord.GitHub.Tests.ProducerFenceCompletionTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open FS.GG.Coord.Types
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub
open FS.GG.Coord.GitHub.Tests.ProducerFenceAttackTests

module Registry = V1AdmissionRegistry

let private ok body =
    { Status = 200
      Body = body
      Headers = Map.empty
      ETag = None
      NextLink = None }

type private QueueProvider(readBodies: string list, mutationBodies: string list) =
    let reads = Queue<string>(readBodies)
    let mutations = Queue<string>(mutationBodies)
    let observed = ResizeArray<Request>()
    let mutable readCount = 0
    let mutable mutationCount = 0

    member _.ReadCount = readCount
    member _.MutationCount = mutationCount
    member _.Observed = List.ofSeq observed

    interface IProviderGitHubTransport with
        member _.Send(request) =
            readCount <- readCount + 1
            observed.Add request

            if reads.Count = 0 then
                failwith $"unscripted provider read: {request.Subject}"

            Ok(ok (reads.Dequeue()))

        member _.SendMutationOnce(request) =
            mutationCount <- mutationCount + 1
            observed.Add request

            if mutations.Count = 0 then
                failwith $"unscripted provider mutation: {request.Subject}"

            Ok(ok (mutations.Dequeue()))

let private fencedWith (runtime: AttackRuntime) (reads: string list) (mutations: string list) =
    let provider = QueueProvider(reads, mutations)
    let fenced = FencedTransport(provider :> IProviderGitHubTransport, runtime.Fence()) :> IGitHubTransport
    fenced, provider

type private CacheSandbox() =
    let path = Path.Combine(Path.GetTempPath(), "fsgg-gs2-08-6-" + Guid.NewGuid().ToString("N"))

    do
        Directory.CreateDirectory path |> ignore
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", path)

    interface IDisposable with
        member _.Dispose() =
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", null)

            try
                Directory.Delete(path, true)
            with _ ->
                ()

let private board: Board.BoardMap =
    { Number = 12
      Id = "PVT_coord"
      Owner = "FS-GG"
      Title = "Coordination"
      Fields =
        Map.ofList
            [ "Status",
              { Id = "PVTSSF_status"
                Type = Board.SingleSelect(Map.ofList [ "Ready", "opt_ready"; "Done", "opt_done" ]) }
              "Estimate", { Id = "PVTF_est"; Type = Board.Number }
              "Blocked by", { Id = "PVTF_blocked"; Type = Board.Text } ] }

let private issueRef =
    { Owner = "FS-GG"
      Repo = "FS.GG.SDD"
      Number = 350 }

[<Fact>]
let ``real Writes REST helper reaches the durable mutation boundary and succeeds`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fenced, provider = fencedWith runtime [] [ "{\"id\":123}" ]
    Assert.Equal(Ok 123L, Writes.postIssueComment fenced issueRef "producer completion")
    Assert.Equal(1, provider.MutationCount)
    Assert.Equal("POST", provider.Observed.Head.Method)

[<Fact>]
let ``real Board add-item callsite reaches the durable mutation boundary and succeeds`` () =
    use _sandbox = new CacheSandbox()
    let runtime = AttackRuntime("OperatingV1")

    let fenced, provider =
        fencedWith
            runtime
            [ "{\"data\":{\"repository\":{\"issue\":{\"projectItems\":{\"nodes\":[]}}}}}"
              "{\"data\":{\"repository\":{\"issue\":{\"id\":\"I_issue42\"}}}}" ]
            [ "{\"data\":{\"addProjectV2ItemById\":{\"item\":{\"id\":\"PVTI_added\"}}}}" ]

    Assert.Equal(Ok(Board.AddedToBoard "PVTI_added"), Board.addItem fenced board "FS-GG" "FS.GG.SDD" 42)
    Assert.Equal(1, provider.MutationCount)

[<Fact>]
let ``real Board single-field callsite reaches the durable mutation boundary and succeeds`` () =
    let runtime = AttackRuntime("OperatingV1")

    let fenced, provider =
        fencedWith runtime [] [ "{\"data\":{\"updateProjectV2ItemFieldValue\":{\"clientMutationId\":null}}}" ]

    Assert.Equal(Ok(), Board.setField fenced board "PVTI_350" "Status" (Board.Set "Done"))
    Assert.Equal(1, provider.MutationCount)

[<Fact>]
let ``real Board batch callsite reaches the durable mutation boundary and succeeds`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fenced, provider = fencedWith runtime [] [ "{\"data\":{\"f0\":{\"clientMutationId\":null}}}" ]
    Assert.Equal(Ok(), Board.setFieldBatch fenced board "PVTI_350" [ "Status", Board.Set "Done" ])
    Assert.Equal(1, provider.MutationCount)

[<Fact>]
let ``real OperationalGraphQl archive callsite reaches the durable mutation boundary and succeeds`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fenced, provider = fencedWith runtime [] [ "{\"data\":{\"a0\":{\"item\":{\"id\":\"PVTI_350\"}}}}" ]
    Assert.Equal(Ok(), OperationalGraphQl.archiveItems fenced "PVT_coord" [ "PVTI_350" ])
    Assert.Equal(1, provider.MutationCount)

[<Fact>]
let ``real Done roll-up REST helper reaches the durable mutation boundary and succeeds`` () =
    use _sandbox = new CacheSandbox()
    let runtime = AttackRuntime("OperatingV1")

    let parentAllDone =
        """{"data":{"repository":{"issue":{"number":350,"state":"OPEN","closedByPullRequestsReferences":{"nodes":[]},"timelineItems":{"nodes":[]},"subIssues":{"totalCount":1,"nodes":[{"number":398,"state":"CLOSED"}]},"projectItems":{"nodes":[{"project":{"number":12},"status":{"name":"In progress"}}]},"parent":null}}}}"""

    let fenced, provider =
        fencedWith
            runtime
            [ parentAllDone
              "{\"number\":350,\"body\":\"- [ ] #398 the only criterion\"}"
              "{\"data\":{\"repository\":{\"issue\":{\"subIssues\":{\"totalCount\":1,\"nodes\":[{\"number\":398,\"state\":\"CLOSED\",\"repository\":{\"nameWithOwner\":\"FS-GG/FS.GG.SDD\"}}]}}}}}"
              "{\"data\":{\"repository\":{\"issue\":{\"projectItems\":{\"nodes\":[{\"id\":\"PVTI_350\",\"project\":{\"number\":12}}]}}}}}" ]
            [ "{\"data\":{\"updateProjectV2ItemFieldValue\":{\"clientMutationId\":null}}}"
              "{\"number\":350,\"state\":\"closed\"}" ]

    match Done.rollUp fenced board "attack-worker" issueRef Done.Completes with
    | Ok [ Done.ParentClosed closed ] -> Assert.Equal(issueRef, closed)
    | other -> failwithf "real Done boundary did not close: %A" other

    Assert.Equal(2, provider.MutationCount)

[<Theory>]
[<InlineData("OperatingV1", "rest", true)>]
[<InlineData("OperatingV1", "graphql", true)>]
[<InlineData("Preparing", "rest", true)>]
[<InlineData("Preparing", "graphql", true)>]
[<InlineData("FreezeRequested", "rest", false)>]
[<InlineData("FreezeRequested", "graphql", false)>]
[<InlineData("Frozen", "rest", false)>]
[<InlineData("Frozen", "graphql", false)>]
[<InlineData("SwitchedV2", "rest", false)>]
[<InlineData("SwitchedV2", "graphql", false)>]
[<InlineData("VerifiedV2", "rest", false)>]
[<InlineData("VerifiedV2", "graphql", false)>]
[<InlineData("OpenV2", "rest", false)>]
[<InlineData("OpenV2", "graphql", false)>]
[<InlineData("ObservingV2", "rest", false)>]
[<InlineData("ObservingV2", "graphql", false)>]
[<InlineData("ContractingV1", "rest", false)>]
[<InlineData("ContractingV1", "graphql", false)>]
[<InlineData("OperatingV2", "rest", false)>]
[<InlineData("OperatingV2", "graphql", false)>]
[<InlineData("RollingBack", "rest", false)>]
[<InlineData("RollingBack", "graphql", false)>]
let ``REST and GraphQL positive controls cross the complete epoch matrix`` phase entry allowed =
    let runtime = AttackRuntime(phase)
    let fenced, provider = fencedWith runtime [] [ response.Body ]
    let request = if entry = "rest" then restRequest "{}" else graphQlRequest "update"
    let result = fenced.SendMutation { EffectId = $"epoch-{entry}"; Request = request }
    Assert.Equal(allowed, Result.isOk result)
    Assert.Equal((if allowed then 1 else 0), provider.MutationCount)

[<Fact>]
let ``actually absent authority tag refuses before provider IO`` () =
    let runtime = AttackRuntime("OperatingV1")
    runtime.SetReadObjects(fun () -> Error "tag refs/tags/fsgg-v1-authority not found")
    let mutable sends = 0

    let result =
        sendDirect (runtime.Fence()) "missing-tag" (Encoding.UTF8.GetBytes "request") (fun () ->
            sends <- sends + 1
            Ok "response")

    Assert.True(Result.isError result)
    Assert.Equal(0, sends)

[<Fact>]
let ``stable but older authority lineage refuses as a rewind`` () =
    let runtime = AttackRuntime("OperatingV1")
    let current = runtime.AuthorityObjects
    let olderBytes = Array.append current.CommitBytes (Encoding.UTF8.GetBytes "older-lineage\n")
    let olderCommit = gitOid "commit" olderBytes

    let older =
        { current with
            FirstHead = olderCommit
            TagTarget = olderCommit
            Commit = olderCommit
            GenesisCommit = olderCommit
            Ancestry = [ olderCommit ]
            CommitBytes = olderBytes }

    runtime.SetReadObjects(fun () -> Ok older)
    runtime.SetRereadHead(fun () -> Ok olderCommit)
    let mutable sends = 0

    let result =
        sendDirect (runtime.Fence()) "older-authority" (Encoding.UTF8.GetBytes "request") (fun () ->
            sends <- sends + 1
            Ok "response")

    Assert.True(Result.isError result)
    Assert.Equal(0, sends)

[<Fact>]
let ``valid typed claim replacement invalidates the prior generation`` () =
    let claimId = "FS-GG/.github#2964:attack-worker"
    let generationOne = claimObservation claimId 1L
    let runtime = AttackRuntime("OperatingV1", claimBinding = (claimId, 1L, generationOne))
    let replacement = claimObservation claimId 2L

    let replacedAuthority =
        { runtime.AuthorityObjects with
            ClaimJournals = Map.ofList [ claimId, replacement ] }

    runtime.SetReadObjects(fun () -> Ok replacedAuthority)
    runtime.SetRereadHead(fun () -> Ok replacedAuthority.FirstHead)
    let mutable sends = 0

    let result =
        sendDirect
            (runtime.Fence(claimGeneration = Some 1L))
            "claim-replaced"
            (Encoding.UTF8.GetBytes "request")
            (fun () ->
                sends <- sends + 1
                Ok "response")

    Assert.True(Result.isError result)
    Assert.Equal(0, sends)

[<Fact>]
let ``Preparing refuses an operation outside the durable sealed cohort`` () =
    let runtime = AttackRuntime("Preparing")

    let scope =
        operationScope "not-in-sealed-cohort" "attack-worker" 1L None
        |> Result.defaultWith (String.concat "," >> failwith)

    let fence = DurableMutationFence(runtime.Authority, runtime.Journal, scope) :> IMutationFence
    let mutable sends = 0

    let result =
        sendDirect fence "nonmember" (Encoding.UTF8.GetBytes "request") (fun () ->
            sends <- sends + 1
            Ok "response")

    Assert.True(Result.isError result)
    Assert.Equal(0, sends)

[<Fact>]
let ``durable operation replacement invalidates an old-handle retry`` () =
    let runtime = AttackRuntime("OperatingV1")
    let oldFence = runtime.Fence()
    let request = Encoding.UTF8.GetBytes "old-operation"
    Assert.Equal(Ok(ProviderFailed "lost"), sendDirect oldFence "replace" request (fun () -> Error "lost"))

    let absence: ProviderReconciliation =
        { Read = fun _ _ _ observed -> Ok(StronglyAbsent(OriginalRequestRetired, bytesDigest observed, digest "9")) }

    Assert.Equal(Ok(), oldFence.Reconcile("replace", absence))

    let replacementContext =
        { context runtime.AuthorityObjects.Commit runtime.AuthorityObjects.ManifestSha256 with
            OperationGeneration = 2L
            IntentDigest = digest "f" }

    let replacement =
        let authorityPort: AuthorityGitPort =
            { ReadObjects = fun () -> Ok runtime.AuthorityObjects
              RereadHead = fun () -> Ok runtime.AuthorityObjects.FirstHead }

        let snapshot =
            Registry.readVerified authorityPort
            |> Result.defaultWith (String.concat "," >> failwith)

        match Registry.admit (Registry.head runtime.Registry) snapshot replacementContext runtime.Registry with
        | RegistryAdmissionAppended value -> value
        | other -> failwithf "replacement admission failed: %A" other

    runtime.AppendRegistry("replace-operation", replacement)
    let mutable retries = 0

    let result =
        oldFence.RetryProvenAbsent(
            "replace",
            (fun _ ->
                retries <- retries + 1
                Ok "response"),
            applied
        )

    Assert.True(Result.isError result)
    Assert.Equal(0, retries)

[<Fact>]
let ``admissions-closing survives a restart and preserves only the admitted operation`` () =
    let runtime = AttackRuntime("OperatingV1")

    let closing =
        match Registry.closeAdmissions (Registry.head runtime.Registry) runtime.Registry with
        | RegistryAppended value -> value
        | other -> failwithf "close failed: %A" other

    runtime.AppendRegistry("durable-close", closing)
    let reopened = DurableMutationFence(runtime.Authority, runtime.Journal, operationScope "attack-op" "attack-worker" 1L None |> Result.defaultWith (String.concat "," >> failwith)) :> IMutationFence
    let mutable sends = 0

    Assert.Equal(
        Ok(AppliedResponse "response"),
        sendDirect reopened "after-restart" (Encoding.UTF8.GetBytes "request") (fun () ->
            sends <- sends + 1
            Ok "response")
    )

    Assert.Equal(1, sends)

    let nonmember =
        operationScope "late-operation" "attack-worker" 1L None
        |> Result.defaultWith (String.concat "," >> failwith)
        |> fun scope -> DurableMutationFence(runtime.Authority, runtime.Journal, scope) :> IMutationFence

    Assert.True(sendDirect nonmember "late" (Encoding.UTF8.GetBytes "request") (fun () -> Ok "response") |> Result.isError)

[<Fact>]
let ``phase change between successive effects refuses the later provider send`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fence = runtime.Fence()
    let mutable sends = 0

    let send () =
        sends <- sends + 1
        Ok "response"

    Assert.Equal(Ok(AppliedResponse "response"), sendDirect fence "first" (Encoding.UTF8.GetBytes "first") send)
    let frozen = authorityObjects "Frozen" runtime.AuthorityObjects.ManifestSha256 None
    runtime.SetReadObjects(fun () -> Ok frozen)
    runtime.SetRereadHead(fun () -> Ok frozen.FirstHead)
    Assert.True(sendDirect fence "second" (Encoding.UTF8.GetBytes "second") send |> Result.isError)
    Assert.Equal(1, sends)

let private writeOptionalString (writer: BinaryWriter) (value: string option) =
    writer.Write(Option.isSome value)
    value |> Option.iter writer.Write

let private readOptionalString (reader: BinaryReader) =
    if reader.ReadBoolean() then Some(reader.ReadString()) else None

let private writeBytes (writer: BinaryWriter) (value: byte array) =
    writer.Write(value.Length)
    writer.Write value

let private readBytes (reader: BinaryReader) = reader.ReadBytes(reader.ReadInt32())

let private saveJournal path (read: RegistryJournalRead) =
    use stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None)
    use writer = new BinaryWriter(stream, Encoding.UTF8, false)
    writer.Write("fsgg.gs2-08.6-journal/1")
    writer.Write(read.Repository)
    writer.Write(read.RepositoryId)
    writer.Write(read.Ref)
    writeOptionalString writer (read.FirstHead |> Option.map Registry.gitObjectIdValue)
    writeOptionalString writer (read.SecondHead |> Option.map Registry.gitObjectIdValue)

    let revision, commits =
        match read.Observation with
        | JournalComplete(revision, commits) -> revision, commits
        | other -> failwithf "persistent attack journal requires a complete observation: %A" other

    writer.Write(revision)
    writer.Write(commits.Length)

    for commit in commits do
        Assert.Equal(registryAddress, commit.Head.Address)
        Assert.True(commit.Checkpoint.IsNone)
        writer.Write(commit.CommitOid)
        writeOptionalString writer commit.ParentOid
        writer.Write(commit.TreeOid)
        writer.Write(commit.OperationId)
        writer.Write(commit.Head.SchemaVersion)
        writer.Write(commit.Head.Generation)
        writer.Write(commit.Head.EventDigest)
        writeOptionalString writer commit.Head.SnapshotDigest
        writer.Write(commit.Head.Terminal)
        writeOptionalString writer commit.Head.PriorHeadDigest
        writer.Write(commit.Head.HeadDigest)
        writeBytes writer commit.HeadBytes
        writeBytes writer commit.Event.Bytes
        writer.Write(commit.Event.Digest)

    let writeMap (values: Map<string, byte array>) =
        writer.Write(values.Count)

        for KeyValue(key, value) in values do
            writer.Write key
            writeBytes writer value

    writeMap read.CommitBytes
    writeMap read.TreeBytes

let private loadJournal path =
    use stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)
    use reader = new BinaryReader(stream, Encoding.UTF8, false)
    Assert.Equal("fsgg.gs2-08.6-journal/1", reader.ReadString())
    let repository = reader.ReadString()
    let repositoryId = reader.ReadInt64()
    let ref = reader.ReadString()
    let first = readOptionalString reader |> Option.map (Registry.gitObjectId >> Result.defaultWith failwith)
    let second = readOptionalString reader |> Option.map (Registry.gitObjectId >> Result.defaultWith failwith)
    let revision = reader.ReadString()

    let commits =
        [ for _ in 1..reader.ReadInt32() do
              let commitOid = reader.ReadString()
              let parentOid = readOptionalString reader
              let treeOid = reader.ReadString()
              let operationId = reader.ReadString()

              let head =
                  { SchemaVersion = reader.ReadInt32()
                    Address = registryAddress
                    Generation = reader.ReadInt64()
                    EventDigest = reader.ReadString()
                    SnapshotDigest = readOptionalString reader
                    Terminal = reader.ReadBoolean()
                    PriorHeadDigest = readOptionalString reader
                    HeadDigest = reader.ReadString() }

              let headBytes = readBytes reader
              let eventBytes = readBytes reader
              let eventDigest = reader.ReadString()

              yield
                  { CommitOid = commitOid
                    ParentOid = parentOid
                    TreeOid = treeOid
                    OperationId = operationId
                    Head = head
                    HeadBytes = headBytes
                    Event = { Bytes = eventBytes; Digest = eventDigest }
                    Checkpoint = None } ]

    let readMap () =
        [ for _ in 1..reader.ReadInt32() -> reader.ReadString(), readBytes reader ] |> Map.ofList

    { Repository = repository
      RepositoryId = repositoryId
      Ref = ref
      FirstHead = first
      SecondHead = second
      Observation = JournalComplete(revision, commits)
      CommitBytes = readMap ()
      TreeBytes = readMap () }

type private DiskJournal(path: string, initial: RegistryJournalRead, ?casOutcomes: JournalCompareAndSwapOutcome list) =
    let gate = obj ()
    let mutable outcomes = defaultArg casOutcomes []

    do
        if not (File.Exists path) then
            saveJournal path initial

    member _.Snapshot = lock gate (fun () -> loadJournal path)

    member _.Port: DurableJournal =
        { Read = fun _ -> lock gate (fun () -> loadJournal path)
          CompareAndSwap =
            fun proposal ->
                lock gate (fun () ->
                    let current = loadJournal path

                    let outcome =
                        match outcomes with
                        | head :: tail ->
                            outcomes <- tail
                            head
                        | [] ->
                            let observed = Registry.proposalCas proposal |> _.ObservedObjectId

                            let actual =
                                current.FirstHead
                                |> Option.map Registry.gitObjectIdValue
                                |> Option.defaultValue ""

                            if observed = actual then
                                CompareAndSwapWon
                            else
                                CompareAndSwapParentConflict

                    match outcome with
                    | CompareAndSwapWon
                    | CompareAndSwapResponseUnknown -> saveJournal path (appendRead current proposal)
                    | _ -> ()

                    outcome) }

let private operationFence authority journal owner =
    let scope = operationScope "attack-op" owner 1L None |> Result.defaultWith (String.concat "," >> failwith)
    DurableMutationFence(authority, journal, scope) :> IMutationFence

let private settlementSummary (read: RegistryJournalRead) =
    let commits = commitsOf read
    let statuses = ResizeArray<string>()
    let mutable intents = 0

    for commit in commits do
        use document = JsonDocument.Parse commit.Event.Bytes
        let root = document.RootElement

        match root.GetProperty("kind").GetString() with
        | "intent"
        | "retry" -> intents <- intents + 1
        | "settle" ->
            statuses.Add(root.GetProperty("payload").GetProperty("settlement").GetProperty("status").GetString())
        | _ -> ()

    let registry = Registry.restore read |> Result.defaultWith (String.concat "," >> failwith)
    intents, List.ofSeq statuses, Registry.unresolvedEffects registry

[<Theory>]
[<InlineData("intent", 0, true)>]
[<InlineData("before-send", 0, true)>]
[<InlineData("after-send", 1, true)>]
[<InlineData("before-settlement", 1, true)>]
[<InlineData("after-settlement", 1, false)>]
let ``serialized journal reopen preserves each process-loss cut`` stage expectedProviderEffects expectedUnresolved =
    let runtime = AttackRuntime("OperatingV1")
    let directory = Path.Combine(Path.GetTempPath(), "fsgg-fence-reopen-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory directory |> ignore
    let path = Path.Combine(directory, "journal.bin")

    try
        let outcomes = if stage = "intent" then [ CompareAndSwapResponseUnknown ] else []
        let disk = DiskJournal(path, runtime.Current, outcomes)
        let journal = disk.Port
        let mutable providerEffects = 0

        let authority =
            if stage = "before-send" then
                let mutable rereads = 0

                { runtime.Authority with
                    RereadHead =
                        fun () ->
                            rereads <- rereads + 1
                            if rereads = 1 then Ok runtime.AuthorityObjects.FirstHead else Error "process-lost-before-send" }
            else
                runtime.Authority

        let fence = operationFence authority journal "attack-worker"

        let send () =
            providerEffects <- providerEffects + 1
            if stage = "after-send" then Error "lost-response" else Ok "response"

        let evidence value =
            if stage = "before-settlement" then
                raise (IOException "process-lost-before-settlement")
            applied value

        if stage = "before-settlement" then
            Assert.Throws<IOException>(fun () -> fence.Dispatch("crash-cut", Encoding.UTF8.GetBytes "request", send, evidence) |> ignore)
            |> ignore
        else
            fence.Dispatch("crash-cut", Encoding.UTF8.GetBytes "request", send, evidence) |> ignore

        // New objects read only the serialized bytes; no in-memory registry/fence state is retained.
        let reopenedDisk = DiskJournal(path, genesisRead (digest "0"))
        let reopened = operationFence runtime.Authority reopenedDisk.Port "attack-worker"
        let intents, statuses, unresolved = settlementSummary reopenedDisk.Snapshot
        Assert.Equal(1, intents)
        Assert.Equal(expectedProviderEffects, providerEffects)
        Assert.Equal(expectedUnresolved, unresolved.Length = 1)

        if stage = "after-settlement" then
            Assert.Equal<string list>([ "applied" ], statuses)
        else
            Assert.Empty statuses

        let mutable duplicateEffects = 0

        let duplicate =
            sendDirect reopened "crash-cut" (Encoding.UTF8.GetBytes "request") (fun () ->
                duplicateEffects <- duplicateEffects + 1
                Ok "duplicate")

        Assert.True(Result.isError duplicate)
        Assert.Equal(0, duplicateEffects)
    finally
        Directory.Delete(directory, true)

[<Theory>]
[<InlineData("applied", false)>]
[<InlineData("proven-absent", false)>]
[<InlineData("partial", true)>]
[<InlineData("indeterminate", true)>]
let ``serialized reopen retains exact settlement state and effect count`` (expectedStatus: string) expectedUnresolved =
    let runtime = AttackRuntime("OperatingV1")
    let directory = Path.Combine(Path.GetTempPath(), "fsgg-fence-state-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory directory |> ignore
    let path = Path.Combine(directory, "journal.bin")

    try
        let disk = DiskJournal(path, runtime.Current)
        let fence = operationFence runtime.Authority disk.Port "attack-worker"
        let request = Encoding.UTF8.GetBytes expectedStatus

        if expectedStatus = "proven-absent" then
            Assert.Equal(Ok(ProviderFailed "lost"), sendDirect fence expectedStatus request (fun () -> Error "lost"))

            let provider: ProviderReconciliation =
                { Read =
                    fun _ _ _ observed ->
                        Ok(StronglyAbsent(ConditionalFenceExcluded, bytesDigest observed, digest "9")) }

            Assert.Equal(Ok(), fence.Reconcile(expectedStatus, provider))
        else
            let evidence _ =
                match expectedStatus with
                | "applied" -> applied "response"
                | "partial" -> Partial "provider-partial"
                | "indeterminate" -> Indeterminate "provider-unknown"
                | other -> failwith other

            fence.Dispatch(expectedStatus, request, (fun () -> Ok "response"), evidence) |> ignore

        let reopened = DiskJournal(path, genesisRead (digest "0"))
        let intents, statuses, unresolved = settlementSummary reopened.Snapshot
        Assert.Equal(1, intents)
        Assert.Equal<string list>([ expectedStatus ], statuses)
        Assert.Equal(expectedUnresolved, unresolved.Length = 1)
    finally
        Directory.Delete(directory, true)

[<Fact>]
let ``two owners racing the same durable parent produce one CAS winner and one provider effect`` () =
    let runtime = AttackRuntime("OperatingV1")
    let gate = obj ()
    use initialReads = new CountdownEvent(2)
    use ownerAWon = new ManualResetEventSlim(false)
    let mutable current = runtime.Current

    let intentOwner proposal =
        use document = JsonDocument.Parse((Registry.proposalCas proposal).ProposedCommit.Event.Bytes)
        let root = document.RootElement

        if root.GetProperty("kind").GetString() = "intent" then
            Some(root.GetProperty("payload").GetProperty("intent").GetProperty("owner").GetString())
        else
            None

    let journal: DurableJournal =
        { Read =
            fun _ ->
                let snapshot = lock gate (fun () -> current)

                if initialReads.CurrentCount > 0 then
                    initialReads.Signal() |> ignore

                snapshot
          CompareAndSwap =
            fun proposal ->
                Assert.True(initialReads.Wait(TimeSpan.FromSeconds 5.0), "both owners must read the same parent")

                if intentOwner proposal = Some "owner-a" then
                    lock gate (fun () -> current <- appendRead current proposal)
                    ownerAWon.Set()
                    CompareAndSwapWon
                elif intentOwner proposal = Some "owner-b" then
                    Assert.True(ownerAWon.Wait(TimeSpan.FromSeconds 5.0), "the designated winner must commit first")
                    CompareAndSwapParentConflict
                else
                    lock gate (fun () ->
                        let observed = Registry.proposalCas proposal |> _.ObservedObjectId
                        let actual = current.FirstHead |> Option.map Registry.gitObjectIdValue |> Option.defaultValue ""

                        if observed = actual then
                            current <- appendRead current proposal
                            CompareAndSwapWon
                        else
                            CompareAndSwapParentConflict) }

    let fence owner = operationFence runtime.Authority journal owner
    let mutable providerEffects = 0

    let dispatch owner =
        Task.Run(fun () ->
            sendDirect (fence owner) "same-parent" (Encoding.UTF8.GetBytes "request") (fun () ->
                Interlocked.Increment(&providerEffects) |> ignore
                Ok "response"))

    let ownerA, ownerB = dispatch "owner-a", dispatch "owner-b"
    Task.WaitAll [| ownerA :> Task; ownerB :> Task |]
    let results = [ ownerA.Result; ownerB.Result ]
    Assert.Single(results |> List.filter Result.isOk) |> ignore
    Assert.Single(results |> List.filter Result.isError) |> ignore
    Assert.Equal(1, providerEffects)
    let intents, statuses, unresolved = settlementSummary current
    Assert.Equal(1, intents)
    Assert.Equal<string list>([ "applied" ], statuses)
    Assert.Empty unresolved

type private ConditionalShaProvider() =
    let mutable currentSha = "sha-old"
    let mutable attempts = 0
    let mutable appliedEffects = 0

    member _.Attempts = attempts
    member _.AppliedEffects = appliedEffects

    member _.Reconciliation: ProviderReconciliation =
        { Read =
            fun _ _ _ bytes ->
                use document = JsonDocument.Parse bytes
                let expected = document.RootElement.GetProperty("ifNoneMatch").GetString()

                if expected <> currentSha then
                    Ok(StronglyAbsent(ConditionalFenceExcluded, bytesDigest bytes, digest "8"))
                else
                    Ok(Indeterminate "conditional target still equals the delayed request SHA") }

    interface IProviderGitHubTransport with
        member _.Send _ = Error(Transport "reads are outside this conditional mutation probe")

        member _.SendMutationOnce request =
            attempts <- attempts + 1

            if attempts = 1 then
                currentSha <- "sha-new"
                Error(Transport "response lost after the provider advanced away from sha-old")
            elif request.IfNoneMatch <> Some currentSha then
                Ok
                    { Status = 412
                      Body = "conditional SHA no longer matches"
                      Headers = Map.empty
                      ETag = None
                      NextLink = None }
            else
                appliedEffects <- appliedEffects + 1
                Ok(ok "{}")

[<Fact>]
let ``delayed old-SHA request exercises conditional exclusion and remains non-applied`` () =
    let runtime = AttackRuntime("OperatingV1")
    let provider = ConditionalShaProvider()
    let fenced = FencedTransport(provider :> IProviderGitHubTransport, runtime.Fence()) :> IGitHubTransport

    let request =
        { restRequest "{\"state\":\"closed\"}" with
            IfNoneMatch = Some "sha-old" }

    let effectId = mutationEffectId "delayed-old-sha" request
    Assert.True(fenced.SendMutation { EffectId = effectId; Request = request } |> Result.isError)
    let fence = runtime.Fence()
    Assert.Equal(Ok(), fence.Reconcile(effectId, provider.Reconciliation))

    // Retry replays the persisted original bytes. The intercepting provider applies its actual SHA
    // condition, returns 412, and the fence records Indeterminate rather than inventing success.
    Assert.True(fenced.RetryMutation(effectId) |> Result.isError)
    Assert.Equal(2, provider.Attempts)
    Assert.Equal(0, provider.AppliedEffects)
    Assert.True(fenced.RetryMutation(effectId) |> Result.isError)
    Assert.Equal(2, provider.Attempts)
