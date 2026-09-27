module FS.GG.Coord.GitHub.Tests.ProtectedIntakeGenesisTests

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.ProtectedIntakeAuthority
open FS.GG.Coord.GitHub.ProtectedIntakeGenesis

let private sha256 (bytes: byte array) =
    SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

let private gitOid kind (bytes: byte array) =
    let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
    SHA1.HashData(Array.append header bytes) |> Convert.ToHexString |> _.ToLowerInvariant()

type private GitResult = { ExitCode: int; Output: byte array; Error: string }

let private git workingDirectory arguments input =
    let start = ProcessStartInfo("git")
    start.UseShellExecute <- false
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    start.RedirectStandardInput <- input |> Option.isSome
    start.WorkingDirectory <- workingDirectory
    arguments |> List.iter start.ArgumentList.Add
    use child = Process.Start start
    input
    |> Option.iter (fun (bytes: byte array) ->
        child.StandardInput.BaseStream.Write(bytes, 0, bytes.Length)
        child.StandardInput.Close())
    use output = new MemoryStream()
    child.StandardOutput.BaseStream.CopyTo output
    let error = child.StandardError.ReadToEnd().Trim()
    child.WaitForExit()
    { ExitCode = child.ExitCode; Output = output.ToArray(); Error = error }

let private requireGit workingDirectory arguments input =
    let result = git workingDirectory arguments input
    if result.ExitCode <> 0 then
        failwithf "git %s failed (%d): %s" (String.concat " " arguments) result.ExitCode result.Error
    result.Output

let private text (bytes: byte array) = Encoding.UTF8.GetString(bytes).Trim()

let private installObject objectDatabase kind expected bytes =
    let actual =
        requireGit "" [ "--git-dir"; objectDatabase; "hash-object"; "-w"; "-t"; kind; "--stdin" ] (Some bytes)
        |> text
    Assert.Equal(expected, actual)

let private installPlan objectDatabase (value: Plan) =
    let proposal = value.Proposal
    installObject objectDatabase "blob" proposal.EventBlobOid proposal.EventBlobBytes
    installObject objectDatabase "blob" proposal.HeadBlobOid proposal.HeadBlobBytes
    installObject objectDatabase "tree" proposal.TreeOid proposal.TreeBytes
    installObject objectDatabase "commit" proposal.CommitOid proposal.CommitBytes

let private readObjects objectDatabase (value: Plan) =
    let proposal = value.Proposal
    { EventBlobOid = proposal.EventBlobOid
      EventBlobBytes = requireGit "" [ "--git-dir"; objectDatabase; "cat-file"; "blob"; proposal.EventBlobOid ] None
      HeadBlobOid = proposal.HeadBlobOid
      HeadBlobBytes = requireGit "" [ "--git-dir"; objectDatabase; "cat-file"; "blob"; proposal.HeadBlobOid ] None
      TreeOid = proposal.TreeOid
      TreeBytes = requireGit "" [ "--git-dir"; objectDatabase; "cat-file"; "tree"; proposal.TreeOid ] None
      CommitOid = proposal.CommitOid
      CommitBytes = requireGit "" [ "--git-dir"; objectDatabase; "cat-file"; "commit"; proposal.CommitOid ] None
      CommitParentOid = None }

let private withBareRepos test =
    let temporary = Path.Combine(Path.GetTempPath(), "fsgg-intake-genesis-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory temporary |> ignore
    try
        let server = Path.Combine(temporary, "authority.git")
        let client = Path.Combine(temporary, "objects.git")
        requireGit "" [ "init"; "--bare"; server ] None |> ignore
        requireGit "" [ "init"; "--bare"; client ] None |> ignore
        test server client
    finally
        Directory.Delete(temporary, true)

let private authorityFixture (genesis: Plan) =
    let now = DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero)
    let policyBytes = Encoding.UTF8.GetBytes "synthetic intake genesis policy; not an accepted anchor"
    let workflowBytes = Encoding.UTF8.GetBytes "synthetic protected intake workflow"
    let workflow =
        { Name = EnvironmentName
          WorkflowRef = "refs/heads/main"
          RequiredReviewerIds = [ 9001L ]
          PreventSelfReview = true }
    let authorizer =
        { KeyId = "synthetic-intake-genesis-key"
          Algorithm = "RSA-PSS-SHA256"
          PublicKeySpkiSha256 = String.replicate 64 "a"
          SigningDomain = SigningDomain }
    let writer =
        { Id = 21872113L
          Name = "v2-journal-writer"
          Target = "branch"
          Enforcement = "active"
          UpdatedAt = "2026-09-24T19:41:44.504000Z"
          Includes = [ "refs/heads/fsgg/v2/journal/**/*" ]
          Excludes = [ "refs/heads/fsgg/v2/journal/cutover/d5" ]
          Rules = [ "creation"; "update" ]
          BypassAppIds = [ 4882140L; 5064713L ] }
    let integrity =
        { Id = 21872115L
          Name = "v2-journal-integrity"
          Target = "branch"
          Enforcement = "active"
          UpdatedAt = "2026-08-30T18:43:34.946000Z"
          Includes = [ "refs/heads/fsgg/v2/journal/**/*" ]
          Excludes = []
          Rules = [ "deletion"; "non_fast_forward" ]
          BypassAppIds = [] }
    let effective =
        [ { RuleType = "creation"; RulesetId = 21872113L; SourceType = "Repository"; Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "update"; RulesetId = 21872113L; SourceType = "Repository"; Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "deletion"; RulesetId = 21872115L; SourceType = "Repository"; Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "non_fast_forward"; RulesetId = 21872115L; SourceType = "Repository"; Source = "FS-GG/FS.GG.Coordination.Authority" } ]
    let policy =
        { Schema = ProtectedIntakeAuthority.Schema
          PolicyId = ProtectedIntakeAuthority.PolicyId
          OperationClass = ProtectedIntakeAuthority.OperationClass
          AggregateId = IntakeTransaction.AggregateId
          Ref = IntakeTransaction.JournalRef
          Intent = Genesis
          Repository = "FS-GG/FS.GG.Coordination.Authority"
          RepositoryId = 1351660651L
          Workflow = workflow
          Authorizer = authorizer
          Writer = writer
          Integrity = integrity
          EffectiveRules = effective
          Source =
            { SourceCommit = String.replicate 40 "b"
              SourceTreeSha256 = String.replicate 64 "c"
              WorkflowPath = WorkflowPath
              WorkflowBytes = workflowBytes
              WorkflowSha256 = sha256 workflowBytes
              PolicySha256 = sha256 policyBytes }
          Proposal = genesis.Proposal
          PolicyBytes = policyBytes }
    let reviewBytes = Encoding.UTF8.GetBytes "synthetic intake genesis review-decision/v2"
    let authorization =
        { Complete = true
          PullRequest = 7002
          HeadCommit = policy.Source.SourceCommit
          BaseCommit = String.replicate 40 "d"
          ReviewDecision = "APPROVED"
          ReviewDecisionV2Bytes = reviewBytes
          ReviewDecisionSha256 = sha256 reviewBytes
          MergeCommit = String.replicate 40 "e"
          AuthorizedAt = now.AddMinutes(-2)
          SourceTreeSha256 = policy.Source.SourceTreeSha256
          PolicyBlobOid = gitOid "blob" policyBytes
          PolicySha256 = policy.Source.PolicySha256
          WorkflowPath = WorkflowPath
          WorkflowSha256 = policy.Source.WorkflowSha256
          Workflow = workflow
          Authorizer = authorizer
          SigningDomain = SigningDomain
          Intent = Genesis
          PlanSha256 = genesis.Proposal.PlanSha256
          CommitOid = genesis.ProposedObjectId }
    let credential =
        { AppId = 5064713L
          InstallationId = 164553252L
          Repository = policy.Repository
          RepositoryId = policy.RepositoryId
          RepositoryIds = [ policy.RepositoryId ]
          Permissions = Map [ "contents", "write"; "metadata", "read" ]
          WorkflowPath = WorkflowPath
          WorkflowRef = workflow.WorkflowRef
          Environment = EnvironmentName
          ExpiresAt = now.AddMinutes 30 }
    let proposal = genesis.Proposal
    let objects =
        { EventBlobOid = proposal.EventBlobOid; EventBlobBytes = proposal.EventBlobBytes
          HeadBlobOid = proposal.HeadBlobOid; HeadBlobBytes = proposal.HeadBlobBytes
          TreeOid = proposal.TreeOid; TreeBytes = proposal.TreeBytes
          CommitOid = proposal.CommitOid; CommitBytes = proposal.CommitBytes
          CommitParentOid = None }
    let readback =
        { Complete = true
          ReadAt = now
          Repository = policy.Repository
          RepositoryId = policy.RepositoryId
          Workflow = workflow
          Authorizer = authorizer
          Credential = credential
          Writer = writer
          Integrity = integrity
          EffectiveRules = effective
          InstalledPolicyBlobOid = gitOid "blob" policyBytes
          InstalledPolicyBytes = policyBytes
          RefBefore = Absent
          RefAfter = Present genesis.ProposedObjectId
          Objects = objects }
    now, policy, authorization, readback

[<Fact>]
let ``genesis plan is deterministic parentless and self-verifying`` () =
    let first, second = plan (), plan ()
    Assert.Equal(first, second)
    Assert.Equal(IntakeTransaction.JournalRef, first.Ref)
    Assert.True(first.ExpectedOldObjectId.IsNone)
    Assert.Equal($"--force-with-lease={IntakeTransaction.JournalRef}:", first.ForceWithLease)
    Assert.True(first.Proposal.ParentOid.IsNone)
    Assert.Equal(first.Proposal.EventBlobOid, gitOid "blob" first.Proposal.EventBlobBytes)
    Assert.Equal(first.Proposal.HeadBlobOid, gitOid "blob" first.Proposal.HeadBlobBytes)
    Assert.Equal(first.Proposal.TreeOid, gitOid "tree" first.Proposal.TreeBytes)
    Assert.Equal(first.Proposal.CommitOid, gitOid "commit" first.Proposal.CommitBytes)

[<Fact>]
let ``expected-absent receive-pack creates once and lost response is proven by exact readback`` () =
    withBareRepos (fun server client ->
        let genesis = plan ()
        installPlan client genesis
        let before = git "" [ "--git-dir"; server; "show-ref"; "--verify"; "--quiet"; genesis.Ref ] None
        Assert.NotEqual(0, before.ExitCode)

        // The successful process response is deliberately discarded. Only the authoritative ref and
        // independently read object bytes are used to reconcile the simulated lost response.
        requireGit "" [ "--git-dir"; client; "push"; genesis.ForceWithLease; server; genesis.Refspec ] None
        |> ignore
        let installed = requireGit "" [ "--git-dir"; server; "rev-parse"; genesis.Ref ] None |> text
        let objects = readObjects server genesis
        Assert.Equal(genesis.ProposedObjectId, installed)
        Assert.True(Result.isOk(validateReadback genesis Absent (Present installed) objects)))

[<Fact>]
let ``expected-absent receive-pack rejects a concurrently created ref`` () =
    withBareRepos (fun server client ->
        let genesis = plan ()
        installPlan client genesis
        let foreignBytes = Array.append genesis.Proposal.CommitBytes (Encoding.UTF8.GetBytes "foreign\n")
        let foreignOid = gitOid "commit" foreignBytes
        installObject client "commit" foreignOid foreignBytes
        requireGit "" [ "--git-dir"; client; "push"; server; $"{foreignOid}:{genesis.Ref}" ] None |> ignore

        let attempted =
            git "" [ "--git-dir"; client; "push"; genesis.ForceWithLease; server; genesis.Refspec ] None
        Assert.NotEqual(0, attempted.ExitCode)
        let installed = requireGit "" [ "--git-dir"; server; "rev-parse"; genesis.Ref ] None |> text
        Assert.Equal(foreignOid, installed)
        let refused = validateReadback genesis (Present foreignOid) (Present foreignOid) (readObjects client genesis)
        match refused with
        | Error errors -> Assert.Contains(ExpectedAbsentRefNotAbsent, errors)
        | Ok _ -> failwith "a concurrently created ref must not validate as genesis")

[<Fact>]
let ``partial reads and substituted objects remain indeterminate`` () =
    let genesis = plan ()
    let proposal = genesis.Proposal
    let objects =
        { EventBlobOid = proposal.EventBlobOid; EventBlobBytes = proposal.EventBlobBytes
          HeadBlobOid = proposal.HeadBlobOid; HeadBlobBytes = proposal.HeadBlobBytes
          TreeOid = proposal.TreeOid; TreeBytes = proposal.TreeBytes
          CommitOid = proposal.CommitOid
          CommitBytes = Array.append proposal.CommitBytes [| 0uy |]
          CommitParentOid = None }
    match validateReadback genesis (Unknown "pre-read unavailable") (Partial "post-read truncated") objects with
    | Error errors ->
        Assert.Contains(RefReadbackIndeterminate, errors)
        Assert.Contains(ObjectCustodyMismatch, errors)
    | Ok _ -> failwith "partial readback must not validate"

[<Fact>]
let ``authorized validation requires the separate intake policy and native approval`` () =
    let genesis = plan ()
    let now, policy, authorization, readback = authorityFixture genesis
    Assert.True(Result.isOk(validateAuthorized now genesis policy authorization readback))

    let settlement =
        { policy with
            Schema = "fsgg.github.v2-ci-ordinary-settlement-anchor/1"
            OperationClass = "ordinary-post-merge-delivery-settlement" }
    match validateAuthorized now genesis settlement authorization readback with
    | Error errors ->
        Assert.Contains(errors, function IntakeAuthorityRefused failures -> List.contains PolicyIdentityDrift failures | _ -> false)
    | Ok _ -> failwith "ordinary settlement approval must not authorize intake genesis"
