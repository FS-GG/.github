namespace FS.GG.Coord

module RoadmapWorkUnit =
    [<Literal>]
    val PreparationInputSchema: string = "fsgg.roadmap-unit.preparation-input/1"

    [<Literal>]
    val PreparationPlanSchema: string = "fsgg.roadmap-unit.preparation-plan/1"

    [<Literal>]
    val PreparationApplicationSchema: string = "fsgg.roadmap-unit.preparation-application/1"

    [<Literal>]
    val RevisionBindingSchema: string = "fsgg.roadmap-unit.revision-binding/1"

    [<Literal>]
    val AcceptanceInputSchema: string = "fsgg.roadmap-unit.acceptance-input/1"

    [<Literal>]
    val EvidenceIndexSchema: string = "fsgg.roadmap-unit.evidence-index/1"

    type UnitState = Accepted | Unchecked

    type CatalogRow =
        { UnitId: string
          Title: string
          State: UnitState
          Prerequisite: string option
          Gates: string list
          EvidenceObligations: string list
          ContractSha256: string }

    type RoadmapRow =
        { UnitId: string
          Title: string
          Prerequisite: string option
          Gates: string list }

    type AuthorityPin =
        { RoadmapDigest: string
          CatalogDigest: string
          Issue: string }

    type Registration =
        { Id: string
          Kind: string
          Draft: Intake.Draft }

    type PreparationInput =
        { Schema: string
          RoadmapSourceDigest: string
          CatalogSourceDigest: string
          Catalog: CatalogRow list
          RoadmapRow: RoadmapRow
          AuthorityIssue: string
          SddWorkId: string
          RegistrationOwner: string
          RegistrationRepository: string
          RegistrationPaths: string list }

    type PreparationRequest =
        { Schema: string
          AuthorityIssue: string
          SddWorkId: string
          RegistrationOwner: string
          RegistrationRepository: string
          RegistrationPaths: string list }

    type PreparationPlan =
        { Schema: string
          Unit: CatalogRow
          AcceptedPrerequisite: string
          Authority: AuthorityPin
          SddWorkId: string
          Registrations: Registration list
          EvidenceObligations: string list
          Digest: string }

    type AppliedRegistration =
        { Id: string
          Kind: string
          DraftSha256: string
          Issue: string
          IssueUrl: string }

    type PreparationApplication =
        { Schema: string
          UnitId: string
          PlanDigest: string
          Registrations: AppliedRegistration list
          Digest: string }

    type SddObservation =
        { Stage: string
          SubjectRevision: string
          ArtifactJson: string }

    type RevisionIdentities =
        { ImplementationPullRequest: int
          ImplementationCandidate: string
          ImplementationMerge: string
          AcceptancePullRequest: int
          AcceptanceCandidate: string
          AcceptanceMerge: string
          ProtectedMain: string }

    type RevisionBinding =
        { Schema: string
          Repository: string
          Candidate: string
          Merge: string
          CandidateTree: string
          MergeTree: string
          CommandSha256: string
          ExitCode: int
          Digest: string }

    type AcceptanceInput =
        { Schema: string
          Plan: PreparationPlan
          PreparationApplication: PreparationApplication
          Qualification: Qualification.Accepted
          LifecycleRunId: string
          LifecycleUnitId: string
          LifecycleLog: string
          RequiredLifecyclePhases: string list
          ReviewEvidence: string
          ReviewCycleId: string
          ReviewReceipt: string
          SddWorkId: string
          SddObservations: SddObservation list
          Identities: RevisionIdentities
          ImplementationBinding: RevisionBinding
          AcceptanceBinding: RevisionBinding
          AcceptedAt: string }

    type EvidenceEntry =
        { Name: string
          Sha256: string
          Source: string }

    /// A caller-authored acceptance envelope whose internal relationships are coherent. This is not
    /// authority and cannot be rendered as an accepted receipt.
    type AcceptanceCandidate

    /// Opaque capability minted only after the live adapter has completed every authority read.
    type ObservedAcceptance

    /// An accepted receipt can only be constructed by the live BoardOps authority adapter.
    type Accepted

    type Finding =
        | InvalidSchema of expected: string * observed: string
        | InvalidDigest of field: string * observed: string
        | InvalidIdentity of field: string * observed: string
        | DuplicateCatalogUnit of unitId: string
        | NextUnitMissing
        | MultipleNextUnits of unitIds: string list
        | PrerequisiteNotAccepted of unitId: string * prerequisite: string
        | RoadmapIdentityMismatch of field: string
        | DuplicateGate of gate: string
        | EvidenceObligationMissing of obligation: string
        | RegistrationInvalid of registrationId: string * reason: string
        | RegistrationDuplicate of registrationId: string
        | QualificationMismatch of reason: string
        | LifecycleInvalid of reason: string
        | SddObservationInvalid of stage: string * reason: string
        | RevisionIdentityCollapse of left: string * right: string
        | RevisionRelationInvalid of reason: string
        | ReviewEvidenceMissing
        | BoundedPatchInvalid of reason: string
        | AcceptanceBundleInvalid of reason: string

    val inspectPreparation: PreparationInput -> Result<PreparationPlan, Finding list>
    val parsePreparationRequest: bytes: byte array -> Result<PreparationRequest, string list>
    val compilePreparation: roadmapBytes: byte array -> catalogBytes: byte array -> PreparationRequest -> Result<PreparationPlan, Finding list>
    val canonicalPlan: PreparationPlan -> string
    val canonicalIntakeDraft: Registration -> string
    val sealPreparationApplication: plan: PreparationPlan -> registrations: AppliedRegistration list -> Result<PreparationApplication, Finding list>
    val canonicalPreparationApplication: PreparationApplication -> string
    val parsePreparationApplication: bytes: byte array -> Result<PreparationApplication, string list>
    val canonicalRevisionCommand: repository: string -> candidate: string -> merge: string -> string
    val sealRevisionBinding: repository: string -> candidate: string -> merge: string -> candidateTree: string -> mergeTree: string -> exitCode: int -> RevisionBinding
    val canonicalRevisionBinding: RevisionBinding -> string
    val parseRevisionBinding: bytes: byte array -> Result<RevisionBinding, string list>
    val parsePreparationInput: bytes: byte array -> Result<PreparationInput, string list>
    val parsePlan: bytes: byte array -> Result<PreparationPlan, string list>
    val renderPreparation: source: byte array -> PreparationPlan -> Result<string, Finding list>
    val verifyPreparation: source: byte array -> candidate: byte array -> PreparationPlan -> Result<unit, Finding list>

    val inspectAcceptanceCandidate: AcceptanceInput -> Result<AcceptanceCandidate, Finding list>
    val candidateDigest: AcceptanceCandidate -> string
    val canonicalAcceptanceInput: AcceptanceInput -> string
    val parseAcceptanceInput: bytes: byte array -> Result<AcceptanceInput, string list>
    val internal observeAcceptance: AcceptanceCandidate -> ObservedAcceptance
    val internal sealObservedAcceptance: ObservedAcceptance -> Accepted
    val internal acceptedDigest: Accepted -> string
    val internal acceptedBundle: Accepted -> string
    val internal verifyObservedAcceptance: expected: ObservedAcceptance -> bundle: byte array -> Result<Accepted, Finding list>
    val internal acceptedReceipt: Accepted -> byte array
