namespace FS.GG.Coord.GitHub

open System

/// Source contract for a future protected authority installation for the retained intake journal.
/// Validation grants no write capability; it only accepts an exact policy, native authorization,
/// credential observation, and protected object readback for one proposed genesis or append.
module ProtectedIntakeAuthority =
    [<Literal>]
    val Schema: string = "fsgg.github.protected-intake-authority/1"
    [<Literal>]
    val PolicyId: string = "gs2-09-7-retained-intake-production-v1"
    [<Literal>]
    val OperationClass: string = "retained-intake-journal"
    [<Literal>]
    val SigningDomain: string = "fsgg.github.protected-intake-authority/v1"
    [<Literal>]
    val WorkflowPath: string = ".github/workflows/protected-intake-authority.yml"
    [<Literal>]
    val EnvironmentName: string = "retained-intake-production"

    type Intent =
        | Genesis
        | Append of expectedOldHead: string

    type Authorizer =
        { KeyId: string
          Algorithm: string
          PublicKeySpkiSha256: string
          SigningDomain: string }

    type Environment =
        { Name: string
          WorkflowRef: string
          RequiredReviewerIds: int64 list
          PreventSelfReview: bool }

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
        { RuleType: string
          RulesetId: int64
          SourceType: string
          Source: string }

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

    /// Accepts only the intake-specific contract and complete evidence for its exact proposal.
    /// The returned value is evidence of validation and carries no credential or mutation port.
    val validate:
        now: DateTimeOffset ->
        policy: Policy ->
        authorization: NativeAuthorization ->
        readback: ProtectedReadback ->
            Result<Validated, Failure list>
