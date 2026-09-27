namespace FS.GG.Coord.GitHub

open System
open System.IO
open System.Security.Cryptography
open System.Text
open FS.GG.Coord

module ProtectedIntakeAuthority =
    [<Literal>]
    let Schema = "fsgg.github.protected-intake-authority/1"
    [<Literal>]
    let PolicyId = "gs2-09-7-retained-intake-production-v1"
    [<Literal>]
    let OperationClass = "retained-intake-journal"
    [<Literal>]
    let SigningDomain = "fsgg.github.protected-intake-authority/v1"
    [<Literal>]
    let WorkflowPath = ".github/workflows/protected-intake-authority.yml"
    [<Literal>]
    let EnvironmentName = "retained-intake-production"

    let private repository = "FS-GG/FS.GG.Coordination.Authority"
    let private repositoryId = 1351660651L
    let private appId = 5064713L
    let private installationId = 164553252L
    let private workflowRef = "refs/heads/main"

    type Intent = Genesis | Append of expectedOldHead: string
    type Authorizer =
        { KeyId: string; Algorithm: string; PublicKeySpkiSha256: string; SigningDomain: string }
    type Environment =
        { Name: string; WorkflowRef: string; RequiredReviewerIds: int64 list; PreventSelfReview: bool }
    type Credential =
        { AppId: int64
          InstallationId: int64
          Repository: string
          RepositoryId: int64
          RepositoryIds: int64 list
          Permissions: Map<string, string>
          WorkflowPath: string
          WorkflowRef: string
          Environment: string
          ExpiresAt: DateTimeOffset }
    type Ruleset =
        { Id: int64
          Name: string
          Target: string
          Enforcement: string
          UpdatedAt: string
          Includes: string list
          Excludes: string list
          Rules: string list
          BypassAppIds: int64 list }
    type EffectiveRule =
        { RuleType: string; RulesetId: int64; SourceType: string; Source: string }
    type SourceBinding =
        { SourceCommit: string
          SourceTreeSha256: string
          WorkflowPath: string
          WorkflowBytes: byte array
          WorkflowSha256: string
          PolicySha256: string }
    type Proposal =
        { PlanBytes: byte array
          PlanSha256: string
          ParentOid: string option
          EventBlobOid: string
          EventBlobSha256: string
          EventBlobBytes: byte array
          HeadBlobOid: string
          HeadBlobSha256: string
          HeadBlobBytes: byte array
          TreeOid: string
          TreeSha256: string
          TreeBytes: byte array
          CommitOid: string
          CommitSha256: string
          CommitBytes: byte array }
    type Policy =
        { Schema: string
          PolicyId: string
          OperationClass: string
          AggregateId: string
          Ref: string
          Intent: Intent
          Repository: string
          RepositoryId: int64
          Workflow: Environment
          Authorizer: Authorizer
          Writer: Ruleset
          Integrity: Ruleset
          EffectiveRules: EffectiveRule list
          Source: SourceBinding
          Proposal: Proposal
          PolicyBytes: byte array }
    type NativeAuthorization =
        { Complete: bool
          PullRequest: int
          HeadCommit: string
          BaseCommit: string
          ReviewDecision: string
          ReviewDecisionV2Bytes: byte array
          ReviewDecisionSha256: string
          MergeCommit: string
          AuthorizedAt: DateTimeOffset
          SourceTreeSha256: string
          PolicyBlobOid: string
          PolicySha256: string
          WorkflowPath: string
          WorkflowSha256: string
          Workflow: Environment
          Authorizer: Authorizer
          SigningDomain: string
          Intent: Intent
          PlanSha256: string
          CommitOid: string }
    type RefObservation =
        | Absent
        | Present of objectId: string
        | Partial of reason: string
        | Unknown of reason: string
    type ObjectReadback =
        { EventBlobOid: string
          EventBlobBytes: byte array
          HeadBlobOid: string
          HeadBlobBytes: byte array
          TreeOid: string
          TreeBytes: byte array
          CommitOid: string
          CommitBytes: byte array
          CommitParentOid: string option }
    type ProtectedReadback =
        { Complete: bool
          ReadAt: DateTimeOffset
          Repository: string
          RepositoryId: int64
          Workflow: Environment
          Authorizer: Authorizer
          Credential: Credential
          Writer: Ruleset
          Integrity: Ruleset
          EffectiveRules: EffectiveRule list
          InstalledPolicyBlobOid: string
          InstalledPolicyBytes: byte array
          RefBefore: RefObservation
          RefAfter: RefObservation
          Objects: ObjectReadback }
    type Failure =
        | PolicyIdentityDrift
        | IntakeAddressDrift
        | RepositoryDrift
        | WorkflowDrift
        | EnvironmentDrift
        | AuthorizerDrift
        | CredentialDrift
        | CredentialExpired
        | WriterRulesetDrift
        | IntegrityRulesetDrift
        | EffectiveRulesDrift
        | SourceBindingInvalid
        | ProposalInvalid
        | NativeAuthorizationInvalid
        | AuthorizationMismatch
        | ReadbackIncomplete
        | InstalledPolicyMismatch
        | RefReadbackIndeterminate
        | GenesisAlreadyExists
        | AppendParentMismatch
        | ResultingRefMismatch
        | ObjectReadbackMismatch
    type Validated = private Validated of policySha256: string * commitOid: string

    let private sha256 (bytes: byte array) =
        SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()

    let private gitOid kind (bytes: byte array) =
        let header = Encoding.ASCII.GetBytes($"{kind} {bytes.Length}\u0000")
        SHA1.HashData(Array.append header bytes) |> Convert.ToHexString |> _.ToLowerInvariant()

    let private expectedTreeBytes (eventOid: string) (headOid: string) =
        use stream = new MemoryStream()
        for name, oid in [ "event.json", eventOid; "head.json", headOid ] do
            let entry = Encoding.UTF8.GetBytes("100644 " + name + "\u0000")
            stream.Write(entry, 0, entry.Length)
            let rawOid = Convert.FromHexString oid
            stream.Write(rawOid, 0, rawOid.Length)
        stream.ToArray()

    let private commitLinksProposal (proposal: Proposal) =
        let text = Encoding.UTF8.GetString proposal.CommitBytes
        let lines = text.Split('\n') |> Array.toList
        let parents =
            lines
            |> List.choose (fun line ->
                if line.StartsWith("parent ", StringComparison.Ordinal) then Some(line.Substring(7)) else None)
        List.contains ("tree " + proposal.TreeOid) lines
        && parents = (proposal.ParentOid |> Option.toList)

    let private validHex length (value: string) =
        not (String.IsNullOrWhiteSpace value)
        && value.Length = length
        && value |> Seq.forall (fun c -> c >= '0' && c <= '9' || c >= 'a' && c <= 'f')

    let private exactEnvironment (value: Environment) =
        value.Name = EnvironmentName
        && value.WorkflowRef = workflowRef
        && value.PreventSelfReview
        && not value.RequiredReviewerIds.IsEmpty
        && value.RequiredReviewerIds = List.distinct value.RequiredReviewerIds
        && value.RequiredReviewerIds |> List.forall ((<) 0L)

    let private exactAuthorizer (value: Authorizer) =
        not (String.IsNullOrWhiteSpace value.KeyId)
        && value.Algorithm = "RSA-PSS-SHA256"
        && validHex 64 value.PublicKeySpkiSha256
        && value.SigningDomain = SigningDomain

    let private exactWriter (value: Ruleset) =
        value.Id = 21872113L
        && value.Name = "v2-journal-writer"
        && value.Target = "branch"
        && value.Enforcement = "active"
        && value.UpdatedAt = "2026-09-24T19:41:44.504000Z"
        && value.Includes = [ "refs/heads/fsgg/v2/journal/**/*" ]
        && value.Excludes = [ "refs/heads/fsgg/v2/journal/cutover/d5" ]
        && value.Rules = [ "creation"; "update" ]
        && value.BypassAppIds = [ 4882140L; 5064713L ]

    let private exactIntegrity (value: Ruleset) =
        value.Id = 21872115L
        && value.Name = "v2-journal-integrity"
        && value.Target = "branch"
        && value.Enforcement = "active"
        && value.UpdatedAt = "2026-08-30T18:43:34.946000Z"
        && value.Includes = [ "refs/heads/fsgg/v2/journal/**/*" ]
        && value.Excludes = []
        && value.Rules = [ "deletion"; "non_fast_forward" ]
        && value.BypassAppIds = []

    let private exactCredential (value: Credential) =
        value.AppId = appId
        && value.InstallationId = installationId
        && value.Repository = repository
        && value.RepositoryId = repositoryId
        && value.RepositoryIds = [ repositoryId ]
        && value.Permissions = Map [ "contents", "write"; "metadata", "read" ]
        && value.WorkflowPath = WorkflowPath
        && value.WorkflowRef = workflowRef
        && value.Environment = EnvironmentName

    let private proposalValid (proposal: Proposal) =
        validHex 64 proposal.PlanSha256
        && validHex 40 proposal.EventBlobOid
        && validHex 64 proposal.EventBlobSha256
        && validHex 40 proposal.HeadBlobOid
        && validHex 64 proposal.HeadBlobSha256
        && validHex 40 proposal.TreeOid
        && validHex 64 proposal.TreeSha256
        && validHex 40 proposal.CommitOid
        && validHex 64 proposal.CommitSha256
        && proposal.ParentOid |> Option.forall (validHex 40)
        && sha256 proposal.PlanBytes = proposal.PlanSha256
        && gitOid "blob" proposal.EventBlobBytes = proposal.EventBlobOid
        && sha256 proposal.EventBlobBytes = proposal.EventBlobSha256
        && gitOid "blob" proposal.HeadBlobBytes = proposal.HeadBlobOid
        && sha256 proposal.HeadBlobBytes = proposal.HeadBlobSha256
        && gitOid "tree" proposal.TreeBytes = proposal.TreeOid
        && sha256 proposal.TreeBytes = proposal.TreeSha256
        && proposal.TreeBytes = expectedTreeBytes proposal.EventBlobOid proposal.HeadBlobOid
        && gitOid "commit" proposal.CommitBytes = proposal.CommitOid
        && sha256 proposal.CommitBytes = proposal.CommitSha256
        && commitLinksProposal proposal

    let private sourceValid (policy: Policy) =
        let source = policy.Source
        validHex 40 source.SourceCommit
        && validHex 64 source.SourceTreeSha256
        && source.WorkflowPath = WorkflowPath
        && source.WorkflowBytes.Length > 0
        && validHex 64 source.WorkflowSha256
        && sha256 source.WorkflowBytes = source.WorkflowSha256
        && validHex 64 source.PolicySha256
        && source.PolicySha256 = sha256 policy.PolicyBytes

    let private nativeValid (authorization: NativeAuthorization) =
        authorization.Complete
        && authorization.PullRequest > 0
        && validHex 40 authorization.HeadCommit
        && validHex 40 authorization.BaseCommit
        && authorization.HeadCommit <> authorization.BaseCommit
        && authorization.ReviewDecision = "APPROVED"
        && authorization.ReviewDecisionV2Bytes.Length > 0
        && validHex 64 authorization.ReviewDecisionSha256
        && sha256 authorization.ReviewDecisionV2Bytes = authorization.ReviewDecisionSha256
        && validHex 40 authorization.MergeCommit
        && validHex 64 authorization.SourceTreeSha256
        && validHex 40 authorization.PolicyBlobOid
        && authorization.WorkflowPath = WorkflowPath
        && validHex 64 authorization.WorkflowSha256
        && authorization.SigningDomain = SigningDomain
        && exactEnvironment authorization.Workflow
        && exactAuthorizer authorization.Authorizer

    let private sameIntent (left: Intent) (right: Intent) =
        match left, right with
        | Genesis, Genesis -> true
        | Append leftHead, Append rightHead -> leftHead = rightHead
        | _ -> false

    let private exactEffectiveRules =
        [ { RuleType = "creation"; RulesetId = 21872113L; SourceType = "Repository"; Source = repository }
          { RuleType = "update"; RulesetId = 21872113L; SourceType = "Repository"; Source = repository }
          { RuleType = "deletion"; RulesetId = 21872115L; SourceType = "Repository"; Source = repository }
          { RuleType = "non_fast_forward"; RulesetId = 21872115L; SourceType = "Repository"; Source = repository } ]

    let private objectsMatch (proposal: Proposal) (objects: ObjectReadback) =
        objects.EventBlobOid = proposal.EventBlobOid
        && objects.EventBlobBytes = proposal.EventBlobBytes
        && objects.HeadBlobOid = proposal.HeadBlobOid
        && objects.HeadBlobBytes = proposal.HeadBlobBytes
        && objects.TreeOid = proposal.TreeOid
        && objects.TreeBytes = proposal.TreeBytes
        && objects.CommitOid = proposal.CommitOid
        && objects.CommitBytes = proposal.CommitBytes
        && objects.CommitParentOid = proposal.ParentOid
        && gitOid "blob" objects.EventBlobBytes = objects.EventBlobOid
        && gitOid "blob" objects.HeadBlobBytes = objects.HeadBlobOid
        && gitOid "tree" objects.TreeBytes = objects.TreeOid
        && gitOid "commit" objects.CommitBytes = objects.CommitOid

    let validate (now: DateTimeOffset) (policy: Policy) (authorization: NativeAuthorization) (readback: ProtectedReadback) =
        let failures = ResizeArray<Failure>()
        let add condition failure = if not condition then failures.Add failure

        add
            (policy.Schema = Schema
             && policy.PolicyId = PolicyId
             && policy.OperationClass = OperationClass)
            PolicyIdentityDrift

        let address = ProtectedIntakeJournal.address
        add
            (policy.AggregateId = IntakeTransaction.AggregateId
             && policy.AggregateId = address.CanonicalId
             && policy.Ref = IntakeTransaction.JournalRef
             && policy.Ref = address.Ref)
            IntakeAddressDrift

        add
            (policy.Repository = repository
             && policy.RepositoryId = repositoryId
             && readback.Repository = repository
             && readback.RepositoryId = repositoryId)
            RepositoryDrift

        add
            (policy.Source.WorkflowPath = WorkflowPath
             && readback.Credential.WorkflowPath = WorkflowPath)
            WorkflowDrift
        add (exactEnvironment policy.Workflow && readback.Workflow = policy.Workflow) EnvironmentDrift
        add (exactAuthorizer policy.Authorizer && readback.Authorizer = policy.Authorizer) AuthorizerDrift

        add (exactCredential readback.Credential) CredentialDrift
        add (readback.Credential.ExpiresAt > now) CredentialExpired
        add (exactWriter policy.Writer && readback.Writer = policy.Writer) WriterRulesetDrift
        add (exactIntegrity policy.Integrity && readback.Integrity = policy.Integrity) IntegrityRulesetDrift
        add
            (policy.EffectiveRules = exactEffectiveRules
             && readback.EffectiveRules = policy.EffectiveRules)
            EffectiveRulesDrift
        add (sourceValid policy) SourceBindingInvalid
        add (proposalValid policy.Proposal) ProposalInvalid
        add (nativeValid authorization) NativeAuthorizationInvalid

        add
            (authorization.HeadCommit = policy.Source.SourceCommit
             && authorization.SourceTreeSha256 = policy.Source.SourceTreeSha256
             && authorization.PolicyBlobOid = gitOid "blob" policy.PolicyBytes
             && authorization.PolicySha256 = policy.Source.PolicySha256
             && authorization.WorkflowSha256 = policy.Source.WorkflowSha256
             && authorization.SigningDomain = policy.Authorizer.SigningDomain
             && authorization.Workflow = policy.Workflow
             && authorization.Authorizer = policy.Authorizer
             && sameIntent authorization.Intent policy.Intent
             && authorization.PlanSha256 = policy.Proposal.PlanSha256
             && authorization.CommitOid = policy.Proposal.CommitOid
             && authorization.AuthorizedAt <= readback.ReadAt)
            AuthorizationMismatch

        add readback.Complete ReadbackIncomplete
        add
            (readback.InstalledPolicyBytes = policy.PolicyBytes
             && sha256 readback.InstalledPolicyBytes = policy.Source.PolicySha256
             && gitOid "blob" readback.InstalledPolicyBytes = readback.InstalledPolicyBlobOid)
            InstalledPolicyMismatch

        match policy.Intent with
        | Genesis ->
            add (policy.Proposal.ParentOid.IsNone) AppendParentMismatch
            match readback.RefBefore with
            | Absent -> ()
            | Present _ -> failures.Add GenesisAlreadyExists
            | Partial _ | Unknown _ -> failures.Add RefReadbackIndeterminate
        | Append expectedOldHead ->
            match readback.RefBefore with
            | Partial _ | Unknown _ -> failures.Add RefReadbackIndeterminate
            | observed ->
                add
                    (validHex 40 expectedOldHead
                     && policy.Proposal.ParentOid = Some expectedOldHead
                     && observed = Present expectedOldHead)
                    AppendParentMismatch

        match readback.RefAfter with
        | Partial _ | Unknown _ -> failures.Add RefReadbackIndeterminate
        | observed -> add (observed = Present policy.Proposal.CommitOid) ResultingRefMismatch
        add (objectsMatch policy.Proposal readback.Objects) ObjectReadbackMismatch

        if failures.Count = 0 then
            Ok(Validated(policy.Source.PolicySha256, policy.Proposal.CommitOid))
        else
            Error(List.ofSeq failures)
