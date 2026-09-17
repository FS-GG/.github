module FS.GG.Coord.GitHub.Tests.ProducerFenceAttackTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub

module Registry = V1AdmissionRegistry

let digest character =
    String.replicate 64 character
    |> Registry.sha256Digest
    |> Result.defaultWith failwith

let oid character =
    String.replicate 40 character
    |> Registry.gitObjectId
    |> Result.defaultWith failwith

let bytesDigest (bytes: byte array) =
    SHA256.HashData bytes
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> Registry.sha256Digest
    |> Result.defaultWith failwith

let gitOid kind (bytes: byte array) =
    let header = Encoding.UTF8.GetBytes($"{kind} {bytes.Length}\u0000")

    SHA1.HashData(Array.append header bytes)
    |> Convert.ToHexString
    |> _.ToLowerInvariant()
    |> Registry.gitObjectId
    |> Result.defaultWith failwith

let treeBytes entries =
    entries
    |> List.sortBy fst
    |> List.collect (fun (name, value) ->
        (Encoding.UTF8.GetBytes($"100644 {name}\u0000") |> Array.toList)
        @ (Registry.gitObjectIdValue value |> Convert.FromHexString |> Array.toList))
    |> List.toArray

let authorityObjects phase manifest admissionSeal =
    let trust = digest "b"

    let sealFields =
        match admissionSeal with
        | None -> ""
        | Some(commit, generation, cohort) ->
            $",\"admissionCohortSha256\":\"{Registry.sha256Value cohort}\",\"admissionSealCommit\":\"{Registry.gitObjectIdValue commit}\",\"admissionSealGeneration\":{generation}"

    let schema = if phase = "OperatingV1" then "1" else "2"

    let eventBytes =
        Encoding.UTF8.GetBytes(
            $"{{\"fleetId\":\"fs-gg-production\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"phase\":\"{phase}\",\"schema\":\"fsgg.github-substrate.epoch-event/{schema}\",\"trustAnchorSha256\":\"{Registry.sha256Value trust}\"{sealFields}}}"
        )

    let eventOid = gitOid "blob" eventBytes

    let address =
        ShardedJournalAdapter.address Cutover "fleet-cutover:fs-gg-production"
        |> Result.defaultWith (string >> failwith)

    let headBytes =
        ShardedJournalAdapter.journalHeadBytes
            { SchemaVersion = 1
              Address = address
              Generation = 1L
              EventDigest = ShardedJournalAdapter.sha256 eventBytes
              SnapshotDigest = None
              Terminal = false
              PriorHeadDigest = None
              HeadDigest = "" }

    let headOid = gitOid "blob" headBytes
    let entries = Map.ofList [ "event.json", eventOid; "head.json", headOid ]
    let tree = treeBytes (Map.toList entries)
    let treeOid = gitOid "tree" tree

    let commitBytes =
        Encoding.UTF8.GetBytes(
            $"tree {Registry.gitObjectIdValue treeOid}\nauthor attack <attack@fs.gg> 0 +0000\ncommitter attack <attack@fs.gg> 0 +0000\n\n{phase}\n"
        )

    let commit = gitOid "commit" commitBytes

    { Repository = "FS-GG/FS.GG.Coordination.Authority"
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
      ClaimJournals = Map.empty }

let authorityDescendant (parent: AuthorityGitObjects) (child: AuthorityGitObjects) =
    let original = Encoding.UTF8.GetString child.CommitBytes

    let commitBytes =
        original.Replace(
            "author attack <attack@fs.gg> 0 +0000\n",
            $"parent {Registry.gitObjectIdValue parent.Commit}\nauthor attack <attack@fs.gg> 0 +0000\n"
        )
        |> Encoding.UTF8.GetBytes

    let commit = gitOid "commit" commitBytes

    { child with
        FirstHead = commit
        TagTarget = commit
        Commit = commit
        Parent = Some parent.Commit
        GenesisCommit = parent.GenesisCommit
        Ancestry = commit :: parent.Ancestry
        CommitBytes = commitBytes }

let genesisRead manifest =
    let eventBytes =
        ShardedJournalAdapter.canonicalJson (
            $"{{\"commandId\":\"attack-genesis\",\"kind\":\"initialize\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"payload\":{{}},\"round\":1,\"schema\":\"fsgg.github-substrate.admission-event/1\"}}"
        )
        |> Result.defaultWith failwith

    let eventDigest = ShardedJournalAdapter.sha256 eventBytes

    let provisional =
        { SchemaVersion = 1
          Address = registryAddress
          Generation = 1L
          EventDigest = eventDigest
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
    let tree = treeBytes [ "event.json", eventOid; "head.json", headOid ]
    let treeOid = gitOid "tree" tree

    let commitBytes =
        Encoding.UTF8.GetBytes(
            $"tree {Registry.gitObjectIdValue treeOid}\nauthor attack <attack@fs.gg> 0 +0000\ncommitter attack <attack@fs.gg> 0 +0000\n\nattack genesis\n"
        )

    let commitOid = gitOid "commit" commitBytes

    let commit =
        { CommitOid = Registry.gitObjectIdValue commitOid
          ParentOid = None
          TreeOid = Registry.gitObjectIdValue treeOid
          OperationId = "attack-genesis"
          Head = head
          HeadBytes = headBytes
          Event =
            { Bytes = eventBytes
              Digest = eventDigest }
          Checkpoint = None }

    { Repository = "FS-GG/FS.GG.Coordination.Authority"
      RepositoryId = 1351660651L
      Ref = registryAddress.Ref
      FirstHead = Some commitOid
      SecondHead = Some commitOid
      Observation = JournalComplete("attack", [ commit ])
      CommitBytes = Map.ofList [ commit.CommitOid, commitBytes ]
      TreeBytes = Map.ofList [ commit.TreeOid, tree ] }

let commitsOf read =
    match read.Observation with
    | JournalComplete(_, commits) -> commits
    | _ -> failwith "attack journal must remain complete"

let appendRead read proposal =
    let cas, objects = Registry.proposalCas proposal, Registry.proposalObjects proposal

    { read with
        FirstHead = Some objects.CommitObjectId
        SecondHead = Some objects.CommitObjectId
        Observation = JournalComplete("attack", commitsOf read @ [ cas.ProposedCommit ])
        CommitBytes = Map.add (Registry.gitObjectIdValue objects.CommitObjectId) objects.CommitBytes read.CommitBytes
        TreeBytes = Map.add (Registry.gitObjectIdValue objects.TreeObjectId) objects.TreeBytes read.TreeBytes }

let appendCandidate command current candidate =
    let proposal =
        Registry.planAppend command current candidate
        |> Result.defaultWith (String.concat "," >> failwith)

    appendRead current proposal

let claimObservation claimId generation =
    let address =
        ShardedJournalAdapter.address Claim claimId
        |> Result.defaultWith (string >> failwith)

    let folder (previous: JournalCommit option, commits: JournalCommit list) currentGeneration =
        let eventBytes =
            ShardedJournalAdapter.canonicalJson ($"{{\"claimId\":\"{claimId}\",\"generation\":{currentGeneration}}}")
            |> Result.defaultWith failwith

        let provisional =
            { SchemaVersion = 1
              Address = address
              Generation = currentGeneration
              EventDigest = ShardedJournalAdapter.sha256 eventBytes
              SnapshotDigest = None
              Terminal = false
              PriorHeadDigest = previous |> Option.map _.Head.HeadDigest
              HeadDigest = "" }

        let head =
            { provisional with
                HeadDigest =
                    ShardedJournalAdapter.journalHeadBytes provisional
                    |> ShardedJournalAdapter.sha256 }

        let headBytes = ShardedJournalAdapter.journalHeadBytes head
        let eventOid, headOid = gitOid "blob" eventBytes, gitOid "blob" headBytes
        let tree = treeBytes [ "event.json", eventOid; "head.json", headOid ]
        let treeOid = gitOid "tree" tree

        let parentLine =
            previous
            |> Option.map (fun value -> $"parent {value.CommitOid}\n")
            |> Option.defaultValue ""

        let commitBytes =
            Encoding.UTF8.GetBytes(
                $"tree {Registry.gitObjectIdValue treeOid}\n{parentLine}author attack <attack@fs.gg> 0 +0000\ncommitter attack <attack@fs.gg> 0 +0000\n\nclaim {currentGeneration}\n"
            )

        let commit =
            { CommitOid = Registry.gitObjectIdValue (gitOid "commit" commitBytes)
              ParentOid = previous |> Option.map _.CommitOid
              TreeOid = Registry.gitObjectIdValue treeOid
              OperationId = $"claim-{currentGeneration}"
              Head = head
              HeadBytes = headBytes
              Event = { Bytes = eventBytes; Digest = ShardedJournalAdapter.sha256 eventBytes }
              Checkpoint = None }

        Some commit, commits @ [ commit ]

    let _, commits = [ 1L..generation ] |> List.fold folder (None, [])
    JournalComplete("claim-attack", commits)

let context commit manifest =
    { Round = 1L
      Manifest = manifest
      OperationId = "attack-op"
      OperationGeneration = 1L
      Actor = "attack-worker"
      Receiver = "coordination"
      Kind = "producer-boundary-attack"
      CanonicalTarget = "FS-GG/.github#2964"
      Claim = NoClaimRequired
      IntentDigest = digest "d"
      TouchSetDigest = digest "e"
      OriginatingEpochCommit = commit
      OriginatingEpochGeneration = 1L }

type AttackRuntime(
    phase: string,
    ?casOutcomes: JournalCompareAndSwapOutcome list,
    ?claimBinding: string * int64 * JournalObservation
) =
    let manifest = digest "a"
    let claim = claimBinding |> Option.map (fun (id, generation, _) -> TypedClaim(id, generation))
    let claimJournals = claimBinding |> Option.map (fun (id, _, observation) -> Map.ofList [ id, observation ]) |> Option.defaultValue Map.empty
    let operating = { authorityObjects "OperatingV1" manifest None with ClaimJournals = claimJournals }

    let operatingPort: AuthorityGitPort =
        { ReadObjects = fun () -> Ok operating
          RereadHead = fun () -> Ok operating.FirstHead }

    let operatingSnapshot =
        Registry.readVerified operatingPort
        |> Result.defaultWith (String.concat "," >> failwith)

    let mutable current = genesisRead manifest
    let mutable pending = defaultArg casOutcomes []
    let mutable reads = 0

    let mutable readObjectsOverride: (unit -> Result<AuthorityGitObjects, string>) option =
        None

    let mutable rereadOverride: (unit -> Result<GitObjectId, string>) option = None

    let mutable readTransform: int -> RegistryJournalRead -> RegistryJournalRead =
        fun _ value -> value

    do
        let registry =
            Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

        let admitted =
            match
                Registry.admit
                    (Registry.head registry)
                    operatingSnapshot
                    { context operating.Commit manifest with Claim = defaultArg claim NoClaimRequired }
                    registry
            with
            | RegistryAdmissionAppended value -> value
            | other -> failwithf "attack admission failed: %A" other

        current <- appendCandidate "attack-admit" current admitted

    let authority =
        if phase = "Preparing" then
            let admitted =
                Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

            let closing =
                match Registry.closeAdmissions (Registry.head admitted) admitted with
                | RegistryAppended value -> value
                | other -> failwithf "attack close failed: %A" other

            current <- appendCandidate "attack-close" current closing

            let closed =
                Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

            let sealedRegistry =
                match Registry.sealAdmissions (Registry.head closed) operatingSnapshot closed with
                | RegistryAppended value -> value
                | other -> failwithf "attack seal failed: %A" other

            current <- appendCandidate "attack-seal" current sealedRegistry

            let durable =
                Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

            let seal =
                Registry.preparingReference durable
                |> Result.defaultWith (String.concat "," >> failwith)

            { authorityDescendant operating (authorityObjects phase manifest (Some seal)) with
                ClaimJournals = claimJournals }
        else
            { authorityObjects phase manifest None with ClaimJournals = claimJournals }

    member _.AuthorityObjects = authority
    member _.Current = current

    member _.ReplaceCurrent(value: RegistryJournalRead) = current <- value

    member _.AppendRegistry(command: string, candidate: AdmissionRegistry) =
        current <- appendCandidate command current candidate

    member _.Registry =
        Registry.restore current |> Result.defaultWith (String.concat "," >> failwith)

    member _.SetReadObjects(value) = readObjectsOverride <- Some value
    member _.SetRereadHead(value) = rereadOverride <- Some value

    member _.SetJournalRead(value) =
        reads <- 0
        readTransform <- value

    member _.Authority: DurableAuthority =
        { ReadObjects = fun () -> defaultArg readObjectsOverride (fun () -> Ok authority) ()
          RereadHead = fun () -> defaultArg rereadOverride (fun () -> Ok authority.FirstHead) () }

    member _.Journal: DurableJournal =
        { Read =
            fun _ ->
                reads <- reads + 1
                readTransform reads current
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

                outcome }

    member this.Fence(?owner: string, ?operationGeneration: int64, ?claimGeneration: int64 option) =
        let scope =
            operationScope
                "attack-op"
                (defaultArg owner "attack-worker")
                (defaultArg operationGeneration 1L)
                (defaultArg claimGeneration None)
            |> Result.defaultWith (String.concat "," >> failwith)

        DurableMutationFence(this.Authority, this.Journal, scope) :> IMutationFence

type ProviderProbe(response: Response) =
    let mutable reads = 0
    let mutable mutations = 0
    member _.Reads = reads
    member _.Mutations = mutations

    interface IProviderGitHubTransport with
        member _.Send _ =
            reads <- reads + 1
            Ok response

        member _.SendMutationOnce _ =
            mutations <- mutations + 1
            Ok response

let response =
    { Status = 200
      Body = "{\"data\":{\"update\":{\"id\":\"1\"}}}"
      Headers = Map.empty
      ETag = None
      NextLink = None }

let restRequest body =
    { Method = "PATCH"
      Path = "repos/FS-GG/.github/issues/2964"
      Query = [ "z", "last"; "a", "first" ]
      Body = Json body
      Budget = Rest
      IfNoneMatch = Some "attack-etag"
      Subject = "attack-rest" }

let graphQlRequest name =
    { Method = "POST"
      Path = "graphql"
      Query = []
      Body = Query($"mutation {{ {name}(input: {{}}) {{ id }} }}", [ "owner", VString "FS-GG" ])
      Budget = GraphQl
      IfNoneMatch = None
      Subject = "attack-graphql" }

let applied (body: string) =
    Applied(bytesDigest (Encoding.UTF8.GetBytes body))

let sendDirect
    (fence: IMutationFence)
    (effect: string)
    (bytes: byte array)
    (callback: unit -> Result<string, string>)
    =
    fence.Dispatch(effect, bytes, callback, fun value -> applied value)

let transport (runtime: AttackRuntime) =
    let probe = ProviderProbe response

    let value =
        FencedTransport(probe :> IProviderGitHubTransport, runtime.Fence()) :> IGitHubTransport

    value, probe

[<Theory>]
[<InlineData("OperatingV1", true)>]
[<InlineData("Preparing", true)>]
[<InlineData("FreezeRequested", false)>]
[<InlineData("Frozen", false)>]
[<InlineData("SwitchedV2", false)>]
[<InlineData("VerifiedV2", false)>]
[<InlineData("OpenV2", false)>]
[<InlineData("ObservingV2", false)>]
[<InlineData("ContractingV1", false)>]
[<InlineData("OperatingV2", false)>]
[<InlineData("RollingBack", false)>]
let ``all eleven epochs enforce the independent allow-refuse oracle`` phase expectedSend =
    let runtime = AttackRuntime(phase)
    let fenced, probe = transport runtime

    let result =
        fenced.SendMutation
            { EffectId = "epoch"
              Request = restRequest "{}" }

    Assert.Equal(expectedSend, Result.isOk result)
    Assert.Equal((if expectedSend then 1 else 0), probe.Mutations)

[<Fact>]
let ``stale cache and ledger rewind both refuse before provider I O`` () =
    for staleHead in [ oid "c"; oid "0" ] do
        let runtime = AttackRuntime("OperatingV1")
        runtime.SetRereadHead(fun () -> Ok staleHead)
        let mutable sends = 0

        let result =
            sendDirect (runtime.Fence()) "stale" (Encoding.UTF8.GetBytes "request") (fun () ->
                sends <- sends + 1
                Ok "response")

        Assert.True(Result.isError result)
        Assert.Equal(0, sends)

[<Fact>]
let ``missing tag wrong manifest and permission loss fail closed`` () =
    let attacks =
        [ fun (runtime: AttackRuntime) ->
              let objects = runtime.AuthorityObjects
              runtime.SetReadObjects(fun () -> Ok { objects with TagTarget = oid "f" })
          fun runtime ->
              let objects = runtime.AuthorityObjects

              runtime.SetReadObjects(fun () ->
                  Ok
                      { objects with
                          ManifestSha256 = digest "f" })
          fun runtime -> runtime.SetReadObjects(fun () -> Error "permission-denied") ]

    for attack in attacks do
        let runtime = AttackRuntime("OperatingV1")
        attack runtime
        let mutable sends = 0

        let result =
            sendDirect (runtime.Fence()) "authority" (Encoding.UTF8.GetBytes "request") (fun () ->
                sends <- sends + 1
                Ok "response")

        Assert.True(Result.isError result)
        Assert.Equal(0, sends)

[<Fact>]
let ``crash before send and crash after send cannot duplicate provider mutation`` () =
    let before = AttackRuntime("OperatingV1", [ CompareAndSwapResponseUnknown ])
    let mutable beforeSends = 0

    let beforeResult =
        sendDirect (before.Fence()) "before" (Encoding.UTF8.GetBytes "request") (fun () ->
            beforeSends <- beforeSends + 1
            Ok "response")

    Assert.True(Result.isError beforeResult)
    Assert.Equal(0, beforeSends)

    let after =
        AttackRuntime("OperatingV1", [ CompareAndSwapWon; CompareAndSwapParentConflict ])

    let mutable afterSends = 0

    let callback () =
        afterSends <- afterSends + 1
        Ok "response"

    Assert.True(
        sendDirect (after.Fence()) "after" (Encoding.UTF8.GetBytes "request") callback
        |> Result.isError
    )

    Assert.True(
        sendDirect (after.Fence()) "after" (Encoding.UTF8.GetBytes "request") callback
        |> Result.isError
    )

    Assert.Equal(1, afterSends)

[<Fact>]
let ``lost response concurrent owner and changed generations refuse replay`` () =
    let runtime = AttackRuntime("OperatingV1")
    let request = Encoding.UTF8.GetBytes "request"

    let first =
        sendDirect (runtime.Fence()) "lost" request (fun () -> Error "lost-response")

    Assert.Equal(Ok(ProviderFailed "lost-response"), first)

    let mutable sends = 0

    let callback () =
        sends <- sends + 1
        Ok "response"

    Assert.True(
        sendDirect (runtime.Fence(owner = "other")) "lost" request callback
        |> Result.isError
    )

    Assert.True(
        sendDirect (runtime.Fence(operationGeneration = 2L)) "new-generation" request callback
        |> Result.isError
    )

    Assert.True(
        sendDirect (runtime.Fence(claimGeneration = Some 2L)) "new-claim" request callback
        |> Result.isError
    )

    Assert.Equal(0, sends)

[<Fact>]
let ``same semantic effect with different request bytes refuses the second send`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fence = runtime.Fence()

    Assert.Equal(
        Ok(ProviderFailed "lost"),
        sendDirect fence "same-field" (Encoding.UTF8.GetBytes "value=A") (fun () -> Error "lost")
    )

    let mutable sends = 0

    let changed =
        sendDirect fence "same-field" (Encoding.UTF8.GetBytes "value=B") (fun () ->
            sends <- sends + 1
            Ok "response")

    Assert.True(Result.isError changed)
    Assert.Equal(0, sends)

[<Fact>]
let ``delayed old SHA absence evidence cannot authorize retry`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fence = runtime.Fence()
    let request = Encoding.UTF8.GetBytes "old-sha-request"
    Assert.Equal(Ok(ProviderFailed "lost"), sendDirect fence "delayed" request (fun () -> Error "lost"))

    let provider: ProviderReconciliation =
        { Read =
            fun _ _ _ _ ->
                Ok(
                    StronglyAbsent(
                        IdempotencyKeyExcluded,
                        bytesDigest (Encoding.UTF8.GetBytes "new-sha-request"),
                        digest "9"
                    )
                ) }

    Assert.True(fence.Reconcile("delayed", provider) |> Result.isError)
    let mutable retries = 0

    Assert.True(
        fence.RetryProvenAbsent(
            "delayed",
            (fun _ ->
                retries <- retries + 1
                Ok "response"),
            applied
        )
        |> Result.isError
    )

    Assert.Equal(0, retries)

[<Fact>]
let ``retry requires ProvenAbsent and obtains a fresh epoch fence`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fence = runtime.Fence()
    let request = Encoding.UTF8.GetBytes "original"
    Assert.Equal(Ok(ProviderFailed "lost"), sendDirect fence "retry" request (fun () -> Error "lost"))
    let mutable retries = 0

    let retry () =
        fence.RetryProvenAbsent(
            "retry",
            (fun _ ->
                retries <- retries + 1
                Ok "response"),
            applied
        )

    Assert.True(retry () |> Result.isError)

    let absence: ProviderReconciliation =
        { Read = fun _ _ _ observed -> Ok(StronglyAbsent(ConditionalFenceExcluded, bytesDigest observed, digest "9")) }

    Assert.Equal(Ok(), fence.Reconcile("retry", absence))
    runtime.SetRereadHead(fun () -> Ok(oid "0"))
    Assert.True(retry () |> Result.isError)
    Assert.Equal(0, retries)

    let freshRuntime = AttackRuntime("OperatingV1")
    let freshFence = freshRuntime.Fence()
    Assert.Equal(Ok(ProviderFailed "lost"), sendDirect freshFence "fresh-retry" request (fun () -> Error "lost"))
    Assert.Equal(Ok(), freshFence.Reconcile("fresh-retry", absence))
    let mutable freshRetries = 0

    Assert.Equal(
        Ok(AppliedResponse "response"),
        freshFence.RetryProvenAbsent(
            "fresh-retry",
            (fun persisted ->
                Assert.Equal<byte>(request, persisted)
                freshRetries <- freshRetries + 1
                Ok "response"),
            applied
        )
    )

    Assert.Equal(1, freshRetries)

[<Fact>]
let ``legacy Send preserves allowlisted reads and refuses disguised mutations`` () =
    let runtime = AttackRuntime("OperatingV1")
    let fenced, probe = transport runtime

    let read =
        { Method = "GET"
          Path = "repos/FS-GG/.github/issues/2964"
          Query = []
          Body = NoBody
          Budget = Rest
          IfNoneMatch = None
          Subject = "read" }

    Assert.True(fenced.Send read |> Result.isOk)

    let disguised =
        { Method = "POST"
          Path = "graphql"
          Query = []
          Body = Query("# query-looking comment\nmutation { deleteProjectV2(input: {}) { clientMutationId } }", [])
          Budget = GraphQl
          IfNoneMatch = None
          Subject = "disguised" }

    Assert.True(fenced.Send disguised |> Result.isError)
    Assert.Equal(1, probe.Reads)
    Assert.Equal(0, probe.Mutations)

[<Fact>]
let ``canonical mutation identity binds exact request bytes`` () =
    let first = restRequest "{\"field\":\"A\"}"
    let same = restRequest "{\"field\":\"A\"}"
    let changed = restRequest "{\"field\":\"B\"}"
    Assert.Equal<byte>(canonicalMutationBytes first, canonicalMutationBytes same)
    Assert.Equal(mutationEffectId "set-field" first, mutationEffectId "set-field" same)
    Assert.NotEqual<string>(mutationEffectId "set-field" first, mutationEffectId "set-field" changed)
