namespace FS.GG.Coord

module Qualification =
    [<Literal>]
    val InputSchema: string = "fsgg.qualification.input/1"

    [<Literal>]
    val ResultSchema: string = "fsgg.qualification.result/1"

    type OperationKind = Analyze | Verify | Ship | Hosted | FixedPoint | Mutation

    type ToolIdentity =
        { Id: string
          Version: string
          Sha256: string }

    type ExecutorIdentity =
        { Id: string
          Role: string
          ImplementationSha256: string }

    type OperationEvidence =
        { Id: string
          Kind: OperationKind
          SubjectRevision: string
          Tool: ToolIdentity
          Executor: ExecutorIdentity
          CommandSha256: string
          ArtifactSha256: string list
          ResultSha256: string
          ReplayResultSha256: string option
          ExitCode: int
          Refusal: string option }

    type Claim =
        { Id: string
          SubjectRevision: string
          RequiredKinds: OperationKind list
          EvidenceIds: string list }

    type MutationEvidence =
        { Id: string
          OperationId: string
          ExpectedRefusal: string
          ObservedRefusal: string
          ProductionImplementationSha256: string
          FixtureImplementationSha256: string
          FixtureExecutorId: string
          FixtureExecutorRole: string }

    type HostedCheck =
        { Scope: string
          Id: string
          SubjectRevision: string
          State: string
          Conclusion: string }

    type HostedObservation =
        { Complete: bool
          Checks: HostedCheck list }

    type Obligation =
        { Id: string
          Kind: string }

    type ObligationDeclaration = NoObligations | Obligation of Obligation

    type ObligationAuthority =
        { CommentId: int64
          Url: string
          Author: string }

    type ObligationObservation =
        { HeadSha: string
          Declarations: ObligationDeclaration list
          Readbacks: ObligationAuthority list }

    type SemanticReview =
        { SubjectRevision: string
          Accepted: bool
          Evidence: string }

    type Input =
        { Schema: string
          Subject: string
          SubjectRevision: string
          CheckoutClean: bool
          ToolManifest: ToolIdentity list
          Executor: ExecutorIdentity
          Operations: OperationEvidence list
          Claims: Claim list
          Mutations: MutationEvidence list
          HostedObservations: HostedObservation list
          Obligations: ObligationObservation
          SemanticReview: SemanticReview }

    type Finding =
        | InvalidSchema of expected: string * observed: string
        | InvalidSubject of string
        | DirtyCheckout
        | InvalidDigest of field: string * value: string
        | DuplicateIdentity of kind: string * id: string
        | MissingOperationKind of OperationKind
        | OperationOrderMismatch of expected: OperationKind list * observed: OperationKind list
        | UndeclaredTool of operationId: string * toolId: string
        | ToolIdentityMismatch of operationId: string * toolId: string
        | WrongExecutor of operationId: string * executorId: string
        | StaleSubject of evidenceId: string * observedRevision: string
        | FailedOperation of operationId: string * exitCode: int
        | UnexpectedRefusal of operationId: string
        | FixedPointMismatch of operationId: string
        | ClaimEvidenceMissing of claimId: string * evidenceId: string
        | ClaimEvidenceMismatch of claimId: string * evidenceId: string
        | InadequateClaimEvidence of claimId: string * kind: OperationKind
        | MutationOperationMissing of mutationId: string * operationId: string
        | MutationRefusalMismatch of mutationId: string
        | MutationFixtureNotIndependent of mutationId: string
        | HostedObservationIncomplete of index: int
        | HostedForeignSubject of checkId: string * observedRevision: string
        | HostedCheckPending of checkId: string
        | HostedSetNotConverged
        | ObligationHeadMismatch of observedHead: string
        | ObligationDeclarationMissing
        | ObligationDeclarationDuplicate of count: int
        | ObligationIdDuplicate of id: string
        | ObligationReadbackMissing
        | ObligationReadbackInvalid
        | SemanticReviewMissing
        | SemanticReviewStale of observedRevision: string

    type Accepted =
        { Schema: string
          Subject: string
          SubjectRevision: string
          ToolCount: int
          OperationCount: int
          ClaimCount: int
          MutationCount: int
          HostedCheckCount: int
          ObligationCount: int
          SemanticReviewEvidence: string
          EvidenceDigest: string
          Digest: string }

    val validate: Input -> Result<Accepted, Finding list>
    val canonicalResult: Accepted -> string
    val parseInput: bytes: byte array -> Result<Input, string list>
