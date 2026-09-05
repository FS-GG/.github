namespace FS.GG.Coord

module RoadmapWorkUnit =
    [<Literal>]
    val PreparationInputSchema: string = "fsgg.roadmap-unit.preparation-input/1"

    [<Literal>]
    val PreparationPlanSchema: string = "fsgg.roadmap-unit.preparation-plan/1"

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
          RegistrationOwner: string
          RegistrationRepository: string
          RegistrationPaths: string list }

    type PreparationRequest =
        { Schema: string
          AuthorityIssue: string
          RegistrationOwner: string
          RegistrationRepository: string
          RegistrationPaths: string list }

    type PreparationPlan =
        { Schema: string
          Unit: CatalogRow
          AcceptedPrerequisite: string
          Authority: AuthorityPin
          Registrations: Registration list
          EvidenceObligations: string list
          Digest: string }

    type SddObservation =
        { Stage: string
          SubjectRevision: string
          ArtifactJson: string }

    type RevisionIdentities =
        { ImplementationCandidate: string
          ImplementationMerge: string
          AcceptanceCandidate: string
          AcceptanceMerge: string
          ProtectedMain: string }

    type RevisionBinding =
        { Candidate: string
          Merge: string
          CandidateTree: string
          MergeTree: string
          Observed: bool
          ArtifactSha256: string }

    type AcceptanceInput =
        { Schema: string
          Plan: PreparationPlan
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

    type Accepted =
        { ReceiptJson: string
          EvidenceIndexJson: string
          BundleJson: string
          Digest: string }

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
    val parsePreparationInput: bytes: byte array -> Result<PreparationInput, string list>
    val parsePlan: bytes: byte array -> Result<PreparationPlan, string list>
    val renderPreparation: source: byte array -> PreparationPlan -> Result<string, Finding list>
    val verifyPreparation: source: byte array -> candidate: byte array -> PreparationPlan -> Result<unit, Finding list>

    val inspectAcceptance: AcceptanceInput -> Result<Accepted, Finding list>
    val canonicalAcceptanceInput: AcceptanceInput -> string
    val parseAcceptanceInput: bytes: byte array -> Result<AcceptanceInput, string list>
    val verifyAcceptance: expected: AcceptanceInput -> bundle: byte array -> Result<Accepted, Finding list>
    val acceptedReceipt: Accepted -> byte array
