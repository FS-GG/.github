open System
open System.IO
open System.Security.Cryptography
open System.Text
open FS.GG.Coord.GitHub.V1Admission
open FS.GG.Coordination.GitHub

module Registry = V1AdmissionRegistry

let fail detail = failwith ("packed-fence: " + detail)
let require condition detail = if not condition then fail detail
let payload = Path.GetFullPath(fsi.CommandLineArgs[1]).TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar

for assembly in [ typeof<DurableMutationFence>.Assembly; typeof<AdmissionRegistry>.Assembly ] do
    let location = Path.GetFullPath assembly.Location
    require (location.StartsWith(payload, StringComparison.Ordinal)) ($"repository output loaded instead of package payload: {location}")
    require (not (location.Contains(string Path.DirectorySeparatorChar + "bin" + string Path.DirectorySeparatorChar))) ($"bin output loaded: {location}")

let digest character = String.replicate 64 character |> Registry.sha256Digest |> Result.defaultWith fail
let oid character = String.replicate 40 character |> Registry.gitObjectId |> Result.defaultWith fail
let bytesDigest (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant() |> Registry.sha256Digest |> Result.defaultWith fail
let gitOid kind (bytes: byte array) =
    let header = Encoding.UTF8.GetBytes($"{kind} {bytes.Length}\u0000")
    SHA1.HashData(Array.append header bytes) |> Convert.ToHexString |> _.ToLowerInvariant() |> Registry.gitObjectId |> Result.defaultWith fail
let treeBytes entries =
    entries |> List.sortBy fst |> List.collect (fun (name, value) ->
        (Encoding.UTF8.GetBytes($"100644 {name}\u0000") |> Array.toList) @
        (Registry.gitObjectIdValue value |> Convert.FromHexString |> Array.toList)) |> List.toArray

let authorityObjects phase manifest admissionSeal =
    let trust = digest "b"
    let sealFields =
        match admissionSeal with
        | None -> ""
        | Some(commit, generation, cohort) ->
            $",\"admissionCohortSha256\":\"{Registry.sha256Value cohort}\",\"admissionSealCommit\":\"{Registry.gitObjectIdValue commit}\",\"admissionSealGeneration\":{generation}"
    let schema = if phase = "OperatingV1" then "1" else "2"
    let eventBytes = Encoding.UTF8.GetBytes($"{{\"fleetId\":\"fs-gg-production\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"phase\":\"{phase}\",\"schema\":\"fsgg.github-substrate.epoch-event/{schema}\",\"trustAnchorSha256\":\"{Registry.sha256Value trust}\"{sealFields}}}")
    let eventOid = gitOid "blob" eventBytes
    let address = ShardedJournalAdapter.address Cutover "fleet-cutover:fs-gg-production" |> Result.defaultWith (string >> fail)
    let headBytes =
        ShardedJournalAdapter.journalHeadBytes
            { SchemaVersion = 1; Address = address; Generation = 1L; EventDigest = ShardedJournalAdapter.sha256 eventBytes
              SnapshotDigest = None; Terminal = false; PriorHeadDigest = None; HeadDigest = "" }
    let headOid = gitOid "blob" headBytes
    let entries = Map.ofList [ "event.json", eventOid; "head.json", headOid ]
    let tree = treeBytes (Map.toList entries)
    let treeOid = gitOid "tree" tree
    let commitBytes = Encoding.UTF8.GetBytes($"tree {Registry.gitObjectIdValue treeOid}\nauthor probe <probe@fs.gg> 0 +0000\ncommitter probe <probe@fs.gg> 0 +0000\n\n{phase}\n")
    let commit = gitOid "commit" commitBytes
    { Repository = "FS-GG/FS.GG.Coordination.Authority"; RepositoryId = 1351660651L
      Ref = "refs/heads/fsgg/v2/journal/cutover/d5"; FirstHead = commit; TagTarget = commit; Commit = commit
      Parent = None; GenesisCommit = commit; Ancestry = [ commit ]; CommitTree = treeOid; CommitBytes = commitBytes
      TreeBytes = tree; TreeEntries = entries; EventBlob = eventOid, eventBytes; HeadBlob = headOid, headBytes
      TrustAnchorSha256 = trust; ManifestSha256 = manifest; ClaimJournals = Map.empty }

let authorityDescendant (parent: AuthorityGitObjects) (child: AuthorityGitObjects) =
    let commitBytes =
        Encoding.UTF8.GetString(child.CommitBytes).Replace(
            "author probe <probe@fs.gg> 0 +0000\n",
            $"parent {Registry.gitObjectIdValue parent.Commit}\nauthor probe <probe@fs.gg> 0 +0000\n") |> Encoding.UTF8.GetBytes
    let commit = gitOid "commit" commitBytes
    { child with FirstHead = commit; TagTarget = commit; Commit = commit; Parent = Some parent.Commit
                 GenesisCommit = parent.GenesisCommit; Ancestry = commit :: parent.Ancestry; CommitBytes = commitBytes }

let genesisRead manifest =
    let eventBytes =
        ShardedJournalAdapter.canonicalJson($"{{\"commandId\":\"probe-genesis\",\"kind\":\"initialize\",\"manifestSha256\":\"{Registry.sha256Value manifest}\",\"payload\":{{}},\"round\":1,\"schema\":\"fsgg.github-substrate.admission-event/1\"}}")
        |> Result.defaultWith fail
    let eventDigest = ShardedJournalAdapter.sha256 eventBytes
    let provisional =
        { SchemaVersion = 1; Address = registryAddress; Generation = 1L; EventDigest = eventDigest; SnapshotDigest = None
          Terminal = false; PriorHeadDigest = None; HeadDigest = String.replicate 64 "0" }
    let head = { provisional with HeadDigest = ShardedJournalAdapter.journalHeadBytes provisional |> ShardedJournalAdapter.sha256 }
    let headBytes = ShardedJournalAdapter.journalHeadBytes head
    let eventOid, headOid = gitOid "blob" eventBytes, gitOid "blob" headBytes
    let tree = treeBytes [ "event.json", eventOid; "head.json", headOid ]
    let treeOid = gitOid "tree" tree
    let commitBytes = Encoding.UTF8.GetBytes($"tree {Registry.gitObjectIdValue treeOid}\nauthor probe <probe@fs.gg> 0 +0000\ncommitter probe <probe@fs.gg> 0 +0000\n\nprobe genesis\n")
    let commitOid = gitOid "commit" commitBytes
    let commit =
        { CommitOid = Registry.gitObjectIdValue commitOid; ParentOid = None; TreeOid = Registry.gitObjectIdValue treeOid
          OperationId = "probe-genesis"; Head = head; HeadBytes = headBytes
          Event = { Bytes = eventBytes; Digest = eventDigest }; Checkpoint = None }
    { Repository = "FS-GG/FS.GG.Coordination.Authority"; RepositoryId = 1351660651L; Ref = registryAddress.Ref
      FirstHead = Some commitOid; SecondHead = Some commitOid; Observation = JournalComplete("probe", [ commit ])
      CommitBytes = Map.ofList [ (commit.CommitOid, commitBytes) ]; TreeBytes = Map.ofList [ (commit.TreeOid, tree) ] }

let commitsOf read = match read.Observation with | JournalComplete(_, commits) -> commits | _ -> fail "journal incomplete"
let appendRead read proposal =
    let cas, objects = Registry.proposalCas proposal, Registry.proposalObjects proposal
    { read with FirstHead = Some objects.CommitObjectId; SecondHead = Some objects.CommitObjectId
                Observation = JournalComplete("probe", commitsOf read @ [ cas.ProposedCommit ])
                CommitBytes = Map.add (Registry.gitObjectIdValue objects.CommitObjectId) objects.CommitBytes read.CommitBytes
                TreeBytes = Map.add (Registry.gitObjectIdValue objects.TreeObjectId) objects.TreeBytes read.TreeBytes }
let appendCandidate command current candidate = Registry.planAppend command current candidate |> Result.defaultWith (String.concat "," >> fail) |> appendRead current
let context commit manifest =
    { Round = 1L; Manifest = manifest; OperationId = "probe-op"; OperationGeneration = 1L; Actor = "probe-worker"
      Receiver = "coordination"; Kind = "bridge-package"; CanonicalTarget = "FS-GG/.github:GS2-08.7-P1"
      Claim = NoClaimRequired; IntentDigest = digest "d"; TouchSetDigest = digest "e"
      OriginatingEpochCommit = commit; OriginatingEpochGeneration = 1L }

type Runtime(phase: string) =
    let manifest = digest "a"
    let operating = authorityObjects "OperatingV1" manifest None
    let operatingPort: AuthorityGitPort =
        { ReadObjects = (fun () -> Ok operating)
          RereadHead = (fun () -> Ok operating.FirstHead) }
    let operatingSnapshot = Registry.readVerified operatingPort |> Result.defaultWith (String.concat "," >> fail)
    let mutable current = genesisRead manifest
    do
        let registry = Registry.restore current |> Result.defaultWith (String.concat "," >> fail)
        let admitted =
            match Registry.admit (Registry.head registry) operatingSnapshot (context operating.Commit manifest) registry with
            | RegistryAdmissionAppended value -> value
            | other -> fail ($"admission failed: {other}")
        current <- appendCandidate "probe-admit" current admitted
    let authority =
        if phase = "Preparing" then
            let admitted = Registry.restore current |> Result.defaultWith (String.concat "," >> fail)
            let closing = match Registry.closeAdmissions (Registry.head admitted) admitted with | RegistryAppended value -> value | other -> fail ($"close failed: {other}")
            current <- appendCandidate "probe-close" current closing
            let closed = Registry.restore current |> Result.defaultWith (String.concat "," >> fail)
            let sealedRegistry = match Registry.sealAdmissions (Registry.head closed) operatingSnapshot closed with | RegistryAppended value -> value | other -> fail ($"seal failed: {other}")
            current <- appendCandidate "probe-seal" current sealedRegistry
            let seal = Registry.restore current |> Result.defaultWith (String.concat "," >> fail) |> Registry.preparingReference |> Result.defaultWith (String.concat "," >> fail)
            authorityDescendant operating (authorityObjects phase manifest (Some seal))
        else authorityObjects phase manifest None
    member _.Fence() =
        let authorityPort: DurableAuthority =
            { ReadObjects = (fun () -> Ok authority)
              RereadHead = (fun () -> Ok authority.FirstHead) }
        let journalPort: DurableJournal =
            { Read = fun _ -> current
              CompareAndSwap = fun proposal -> current <- appendRead current proposal; CompareAndSwapWon }
        let scope = operationScope "probe-op" "probe-worker" 1L None |> Result.defaultWith (String.concat "," >> fail)
        DurableMutationFence(authorityPort, journalPort, scope) :> IMutationFence

let applied (body: string) = Applied(bytesDigest(Encoding.UTF8.GetBytes body))
let dispatch phase effect =
    let runtime = Runtime phase
    let fence = runtime.Fence()
    let mutable sends = 0
    let send () : Result<string, string> = sends <- sends + 1; Ok "provider-applied"
    let result = fence.Dispatch(effect, Encoding.UTF8.GetBytes("request:" + effect), send, applied)
    result, sends, fence

for phase in [ "OperatingV1"; "Preparing" ] do
    let result, sends, _ = dispatch phase ("eligible-" + phase)
    match result with
    | Ok(AppliedResponse "provider-applied") when sends = 1 -> ()
    | other -> fail ($"eligible epoch {phase} did not apply exactly once: {other}, sends={sends}")
printfn "packed-fence: eligible epochs PASS"

let refused, refusedSends, _ = dispatch "Frozen" "refused-frozen"
require (Result.isError refused && refusedSends = 0) ($"refused epoch reached provider: {refused}, sends={refusedSends}")
printfn "packed-fence: refused epoch PASS"

let runtime = Runtime "OperatingV1"
let fence = runtime.Fence()
let mutable settlementSends = 0
let send () : Result<string, string> = settlementSends <- settlementSends + 1; Ok "settled"
let first = fence.Dispatch("settlement", Encoding.UTF8.GetBytes("stable-request"), send, applied)
let second = fence.Dispatch("settlement", Encoding.UTF8.GetBytes("stable-request"), send, applied)
require (Result.isOk first && Result.isError second && settlementSends = 1) ($"applied settlement permitted duplicate send: first={first}, second={second}, sends={settlementSends}")
printfn "packed-fence: settlement PASS"
printfn "packed-fence: PASS"
