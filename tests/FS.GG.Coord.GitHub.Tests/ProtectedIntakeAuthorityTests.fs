module FS.GG.Coord.GitHub.Tests.ProtectedIntakeAuthorityTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Xunit
open FS.GG.Coord
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.ProtectedIntakeAuthority

let private sha256 (bytes: byte array) =
    SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

let private gitOid kind (bytes: byte array) =
    let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
    SHA1.HashData(Array.append header bytes) |> Convert.ToHexString |> _.ToLowerInvariant()

let private treeBytes (eventOid: string) (headOid: string) =
    use stream = new MemoryStream()
    for name, oid in [ "event.json", eventOid; "head.json", headOid ] do
        let entry = Encoding.UTF8.GetBytes("100644 " + name + "\u0000")
        stream.Write(entry, 0, entry.Length)
        let rawOid = Convert.FromHexString oid
        stream.Write(rawOid, 0, rawOid.Length)
    stream.ToArray()

let private fixture intent =
    let now = DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero)
    let policyBytes = Encoding.UTF8.GetBytes "synthetic intake policy fixture; not an installed anchor"
    let eventBytes = Encoding.UTF8.GetBytes "synthetic intake event"
    let headBytes = Encoding.UTF8.GetBytes "synthetic intake head"
    let eventOid, headOid = gitOid "blob" eventBytes, gitOid "blob" headBytes
    let tree = treeBytes eventOid headOid
    let treeOid = gitOid "tree" tree
    let parent =
        match intent with
        | Genesis -> None
        | Append expected -> Some expected
    let commitBytes =
        Encoding.UTF8.GetBytes(
            String.concat "\n"
                [ "tree " + treeOid
                  yield! parent |> Option.map (fun oid -> "parent " + oid) |> Option.toList
                  "author synthetic <fixture@invalid> 0 +0000"
                  "committer synthetic <fixture@invalid> 0 +0000"
                  ""
                  "synthetic intake authority fixture"
                  "" ])
    let commitOid = gitOid "commit" commitBytes
    let workflowSha = sha256 (Encoding.UTF8.GetBytes "synthetic workflow fixture")
    let workflowBytes = Encoding.UTF8.GetBytes "synthetic workflow fixture"
    let planBytes = Encoding.UTF8.GetBytes "synthetic plan fixture"
    let planSha = sha256 planBytes
    let reviewBytes = Encoding.UTF8.GetBytes "synthetic review-decision/v2 fixture"

    let workflow =
        { Name = EnvironmentName
          WorkflowRef = "refs/heads/main"
          RequiredReviewerIds = [ 9001L ]
          PreventSelfReview = true }

    let authorizer =
        { KeyId = "synthetic-intake-key"
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

    let effectiveRules =
        [ { RuleType = "creation"
            RulesetId = 21872113L
            SourceType = "Repository"
            Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "update"
            RulesetId = 21872113L
            SourceType = "Repository"
            Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "deletion"
            RulesetId = 21872115L
            SourceType = "Repository"
            Source = "FS-GG/FS.GG.Coordination.Authority" }
          { RuleType = "non_fast_forward"
            RulesetId = 21872115L
            SourceType = "Repository"
            Source = "FS-GG/FS.GG.Coordination.Authority" } ]

    let proposal =
        { PlanBytes = planBytes
          PlanSha256 = planSha
          ParentOid = parent
          EventBlobOid = eventOid
          EventBlobSha256 = sha256 eventBytes
          EventBlobBytes = eventBytes
          HeadBlobOid = headOid
          HeadBlobSha256 = sha256 headBytes
          HeadBlobBytes = headBytes
          TreeOid = treeOid
          TreeSha256 = sha256 tree
          TreeBytes = tree
          CommitOid = commitOid
          CommitSha256 = sha256 commitBytes
          CommitBytes = commitBytes }

    let policy =
        { Schema = Schema
          PolicyId = PolicyId
          OperationClass = OperationClass
          AggregateId = IntakeTransaction.AggregateId
          Ref = IntakeTransaction.JournalRef
          Intent = intent
          Repository = "FS-GG/FS.GG.Coordination.Authority"
          RepositoryId = 1351660651L
          Workflow = workflow
          Authorizer = authorizer
          Writer = writer
          Integrity = integrity
          EffectiveRules = effectiveRules
          Source =
            { SourceCommit = String.replicate 40 "b"
              SourceTreeSha256 = String.replicate 64 "c"
              WorkflowPath = WorkflowPath
              WorkflowBytes = workflowBytes
              WorkflowSha256 = workflowSha
              PolicySha256 = sha256 policyBytes }
          Proposal = proposal
          PolicyBytes = policyBytes }

    let authorization =
        { Complete = true
          PullRequest = 7001
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
          WorkflowSha256 = workflowSha
          Workflow = workflow
          Authorizer = authorizer
          SigningDomain = SigningDomain
          Intent = intent
          PlanSha256 = proposal.PlanSha256
          CommitOid = proposal.CommitOid }

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
          ExpiresAt = now.AddMinutes(30) }

    let objects =
        { EventBlobOid = proposal.EventBlobOid
          EventBlobBytes = proposal.EventBlobBytes
          HeadBlobOid = proposal.HeadBlobOid
          HeadBlobBytes = proposal.HeadBlobBytes
          TreeOid = proposal.TreeOid
          TreeBytes = proposal.TreeBytes
          CommitOid = proposal.CommitOid
          CommitBytes = proposal.CommitBytes
          CommitParentOid = proposal.ParentOid }

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
          EffectiveRules = effectiveRules
          InstalledPolicyBlobOid = gitOid "blob" policyBytes
          InstalledPolicyBytes = policyBytes
          RefBefore = parent |> Option.map Present |> Option.defaultValue Absent
          RefAfter = Present proposal.CommitOid
          Objects = objects }

    now, policy, authorization, readback

let private failures result =
    match result with
    | Ok _ -> failwith "fixture unexpectedly validated"
    | Error values -> values

[<Fact>]
let ``synthetic complete intake evidence validates without installing an anchor`` () =
    let now, policy, authorization, readback = fixture Genesis
    Assert.True(Result.isOk(validate now policy authorization readback))

[<Fact>]
let ``ordinary settlement and V1 genesis identities are refused`` () =
    let now, policy, authorization, readback = fixture Genesis
    let settlement =
        { policy with
            Schema = "fsgg.github.v2-ci-ordinary-settlement-anchor/1"
            PolicyId = "v2-ci-i1-ordinary-settlement-v1"
            OperationClass = "ordinary-post-merge-delivery-settlement" }
    let v1 =
        { policy with
            Schema = "fsgg.github.v1-admission-genesis/1"
            PolicyId = "v1-admission-genesis-production"
            OperationClass = "v1-admission-genesis" }
    Assert.Contains(PolicyIdentityDrift, failures (validate now settlement authorization readback))
    Assert.Contains(PolicyIdentityDrift, failures (validate now v1 authorization readback))

[<Fact>]
let ``shared and incumbent Apps cannot become the intake credential`` () =
    let now, policy, authorization, readback = fixture Genesis
    for otherApp in [ 4166418L; 4882140L ] do
        let changed = { readback with Credential = { readback.Credential with AppId = otherApp } }
        Assert.Contains(CredentialDrift, failures (validate now policy authorization changed))

[<Fact>]
let ``repository permission and expiry broadening are refused`` () =
    let now, policy, authorization, readback = fixture Genesis
    let broadened =
        { readback with
            Credential =
                { readback.Credential with
                    RepositoryIds = [ 1351660651L; 999L ]
                    Permissions = Map [ "contents", "write"; "metadata", "read"; "issues", "write" ] } }
    let expired =
        { readback with Credential = { readback.Credential with ExpiresAt = now } }
    Assert.Contains(CredentialDrift, failures (validate now policy authorization broadened))
    Assert.Contains(CredentialExpired, failures (validate now policy authorization expired))

[<Fact>]
let ``missing or extra bypass actors preserve writer drift`` () =
    let now, policy, authorization, readback = fixture Genesis
    for roster in [ [ 5064713L ]; [ 4882140L; 5064713L; 999L ] ] do
        let changedPolicy = { policy with Writer = { policy.Writer with BypassAppIds = roster } }
        let changedReadback = { readback with Writer = changedPolicy.Writer }
        Assert.Contains(WriterRulesetDrift, failures (validate now changedPolicy authorization changedReadback))

[<Fact>]
let ``authorization from another signing domain or intent is refused`` () =
    let now, policy, authorization, readback = fixture Genesis
    let otherDomain = { authorization with SigningDomain = "fsgg.github.ordinary-settlement/v1" }
    let otherIntent = { authorization with Intent = Append(String.replicate 40 "1") }
    Assert.Contains(NativeAuthorizationInvalid, failures (validate now policy otherDomain readback))
    Assert.Contains(AuthorizationMismatch, failures (validate now policy otherIntent readback))

[<Fact>]
let ``native approval must be complete and bind decision-v2 bytes`` () =
    let now, policy, authorization, readback = fixture Genesis
    let incomplete = { authorization with Complete = false }
    let substituted =
        { authorization with ReviewDecisionV2Bytes = Encoding.UTF8.GetBytes "different review bytes" }
    Assert.Contains(NativeAuthorizationInvalid, failures (validate now policy incomplete readback))
    Assert.Contains(NativeAuthorizationInvalid, failures (validate now policy substituted readback))

[<Fact>]
let ``workflow environment source and plan bytes are independently bound`` () =
    let now, policy, authorization, readback = fixture Genesis
    let openEnvironment =
        { policy with Workflow = { policy.Workflow with RequiredReviewerIds = [] } }
    let sourceSubstitution =
        { policy with
            Source =
                { policy.Source with
                    WorkflowBytes = Encoding.UTF8.GetBytes "different workflow bytes" } }
    let planSubstitution =
        { policy with
            Proposal =
                { policy.Proposal with
                    PlanBytes = Encoding.UTF8.GetBytes "different plan bytes" } }
    Assert.Contains(EnvironmentDrift, failures (validate now openEnvironment authorization readback))
    Assert.Contains(SourceBindingInvalid, failures (validate now sourceSubstitution authorization readback))
    Assert.Contains(ProposalInvalid, failures (validate now planSubstitution authorization readback))

[<Fact>]
let ``effective rules and complete readback are mandatory`` () =
    let now, policy, authorization, readback = fixture Genesis
    let missingRule = { readback with EffectiveRules = List.tail readback.EffectiveRules }
    let incomplete = { readback with Complete = false }
    Assert.Contains(EffectiveRulesDrift, failures (validate now policy authorization missingRule))
    Assert.Contains(ReadbackIncomplete, failures (validate now policy authorization incomplete))

[<Fact>]
let ``genesis requires proven absence and append requires the exact old head`` () =
    let now, genesis, genesisAuthorization, genesisReadback = fixture Genesis
    let alreadyExists = { genesisReadback with RefBefore = Present(String.replicate 40 "7") }
    Assert.Contains(GenesisAlreadyExists, failures (validate now genesis genesisAuthorization alreadyExists))

    let expected = String.replicate 40 "1"
    let _, append, appendAuthorization, appendReadback = fixture (Append expected)
    let stale = { appendReadback with RefBefore = Present(String.replicate 40 "2") }
    Assert.Contains(AppendParentMismatch, failures (validate now append appendAuthorization stale))

[<Fact>]
let ``partial and unknown ref reads never authorize`` () =
    let now, policy, authorization, readback = fixture Genesis
    let partial = { readback with RefBefore = Partial "truncated" }
    let unknown = { readback with RefAfter = Unknown "response-lost" }
    Assert.Contains(RefReadbackIndeterminate, failures (validate now policy authorization partial))
    Assert.Contains(RefReadbackIndeterminate, failures (validate now policy authorization unknown))

[<Fact>]
let ``provider substituted object identities are refused`` () =
    let now, policy, authorization, readback = fixture Genesis
    let bytes = Encoding.UTF8.GetBytes "provider-authored replacement"
    let substitutedObjects =
        { readback.Objects with
            CommitOid = gitOid "commit" bytes
            CommitBytes = bytes }
    let substituted =
        { readback with
            RefAfter = Present substitutedObjects.CommitOid
            Objects = substitutedObjects }
    Assert.Contains(ResultingRefMismatch, failures (validate now policy authorization substituted))
    Assert.Contains(ObjectReadbackMismatch, failures (validate now policy authorization substituted))

[<Fact>]
let ``installed policy and intake address must be exact`` () =
    let now, policy, authorization, readback = fixture Genesis
    let wrongAddress = { policy with Ref = "refs/heads/fsgg/v2/journal/operation/79" }
    let wrongPolicy =
        { readback with InstalledPolicyBytes = Encoding.UTF8.GetBytes "ordinary settlement anchor" }
    Assert.Contains(IntakeAddressDrift, failures (validate now wrongAddress authorization readback))
    Assert.Contains(InstalledPolicyMismatch, failures (validate now policy authorization wrongPolicy))
