namespace FS.GG.Coord.GitHub

/// Closed supported-route preflight: a pure decision over observed inputs, with no IO, native read or
/// mutation of its own. For a selected route and next stage it returns one typed verdict per registered
/// check: `SupportedForNextStage`, `Unsupported` (an exact reason and who can supply what is missing) or
/// `Unknown` (an exact gap). An `Unknown` is never treated as supported: only a report in which every
/// check is `SupportedForNextStage` has exit code 0, and the three overall verdicts have distinct codes.
///
/// The route registry and the stage set are closed. A route or stage with no registered checks yields
/// `Unknown`, never `SupportedForNextStage`. No verdict, subject or evidence carries payload, nonce, private
/// binding content or content node id. Subjects are issue references, counts and the missing-field list; the Project
/// item id (`PVTI_...`) appears only for a non-issue item, which has no reference to name it by.
module V2Preflight =
    open System

    /// The installed reader's payload byte bound (`strictDocument 131072`).
    val payloadLimitBytes: int

    /// The installed reader's binding document bound (262144 characters).
    val bindingLimitBytes: int

    /// Closed route registry. Only `ProgrammeEnrollmentSuccessor` has registered checks; the others are
    /// registered so that asking for them is an explicit `Unknown`, not an unknown route.
    type Route =
        | ProgrammeEnrollmentSuccessor
        | RoutineDelivery
        | PublicationWriterRegistration

    /// Closed stage set: the next stage the operator is about to run. Each route registers only its own
    /// stages; a route and stage that do not belong together have no checks and are `Unknown`.
    type Stage =
        // ProgrammeEnrollmentSuccessor
        | PlanSuccessor
        | PrepareSuccessor
        | BindRuntime
        // RoutineDelivery
        | OpenPullRequest
        | MergePullRequest
        // PublicationWriterRegistration
        | RegisterWriter
        | RunWriter

    val routes: Route list
    val stages: Stage list
    val routeCode: route: Route -> string
    val stageCode: stage: Stage -> string

    type Check =
        | RouteRegistry
        | ConstructorIdentity
        | PredecessorIntegrity
        | RuntimeBindingIntegrity
        | FactsComplete
        | IssueKind
        | IssueIdentity
        | ProjectMembership
        | RepositoryInBinding
        | NotAlreadyEnrolled
        | RoadmapConflict
        | PlanGrammar
        | PlanBlob
        | RowCap
        | PayloadBound
        | TemplateBound
        | ToolchainLock
        | MemoryHeadroom
        | ProjectAutoAdd
        | AdjacencyChurn
        // BindRuntime observes the successor, not the predecessor
        | SuccessorIntegrity
        | SuccessorExtendsPredecessor
        // RoutineDelivery
        | BaseIsDefaultBranch
        | RoutineBranch
        | MarkerSingle
        | MarkerHeadBound
        | MarkerOperationAllowed
        | ClosingLinkDeclared
        | SinglePullRequest
        | BaseCurrent
        // PublicationWriterRegistration
        | WorkflowReadable
        | WorkflowPublishes
        | RegistryReadable
        | NotYetRegistered
        | WriterRegistered
        | CoverageAudit

    val checkCode: check: Check -> string

    /// The registered checks for a route and stage; empty when none are registered.
    val checksFor: route: Route -> stage: Stage -> Check list

    /// What an unsupported verdict is waiting for. Closed.
    type Supplier =
        | Root
        | ConstructorBuild
        | PredecessorCustody
        | PlanAuthor
        | RequestAuthor
        | ProtocolRelease
        | ToolchainCache
        | HostCapacity
        | SuccessorCustody
        | PullRequestOwner
        | WriterRegistry

    val supplierCode: supplier: Supplier -> string

    type Reason =
        | ConstructorDiffersFromMain
        | PredecessorCommitmentMismatch
        | PredecessorNotSpliceable
        | RuntimeBindingDiffers
        | DuplicateRequestIssue
        | IssueIsPullRequest
        | NodeIdentityMismatch
        | NotProjectMember
        | RepositoryOutsideBinding
        | AlreadyEnrolled
        | ConflictingHumanRoadmap
        | PlanNotAdmitted
        | PlanBlobAbsentOnMain
        | RowCapExceeded
        | PayloadOverBound
        | BindingOverBound
        | LockedHashDiffersFromCache
        | InsufficientMemoryHeadroom
        | SuccessorCommitmentMismatch
        | SuccessorDoesNotExtendPredecessor
        | PullRequestBaseNotDefault
        | BranchNotRoutine
        | MarkerMissingOrAmbiguous
        | MarkerHeadDiffers
        | OperationNotAllowed
        | PullRequestAlreadyOpen
        | PullRequestNotSingle
        | WorkflowHasNoPublicationEffect
        | WriterAlreadyRegistered
        | WriterNotRegistered
        | CoverageUnmet

    val reasonCode: reason: Reason -> string

    /// The named input a gap concerns.
    type Input =
        | SourceRevision
        | ProtectedMain
        | PredecessorPrivate
        | PredecessorPublic
        | RuntimeBinding
        | NativeFacts
        | ProjectItems
        | InstalledReader
        | KnownReaderTable
        | LockedHash
        | CachedHash
        | CgroupMemory
        | HostMemory
        | Registry
        | PlanBlobFact
        | SuccessorPrivate
        | SuccessorMerge
        | SuccessorPublic
        | PullRequestFact
        | OpenPullRequestFact
        | BaseHead
        | RoutinePolicy
        | WorkflowText
        | WriterRegistryText
        | CoverageGate
        /// The planner itself, when it could not produce a successor to measure.
        | Planner

    type GapKind =
        | NotSupplied
        | Unreadable
        | Incomplete
        /// Native facts were supplied rather than read now, so "on main now" cannot be asserted.
        | NotLive
        /// The installed artifact's sha is not in the known table of readers whose grammar is the source grammar.
        | NotInKnownTable
        /// A precondition check failed, so this check could not be decided.
        | NotEvaluated
        /// Unrelated Project item updates were observed in the window; their effect on an apply is unknowable.
        | ChurnObserved of count: int
        /// Unenrolled Project memberships lacking Status, Track or Work level. That any of them breaks the
        /// installed caller is UNPROVEN: the caller worked with such memberships present on 2026-10-30, and only
        /// a sudden rise in bare unenrolled memberships matched the later refusal, whose code was coarse.
        | BareMembershipsObserved of count: int
        | NotRegistered
        /// A native read was attempted and failed (distinct from a file that could not be read).
        | ReadFailed
        /// A 404 on a repository that may be private or invisible to the reader: absence is not established.
        | NotVisible
        /// The pull request's base is no longer the default branch head, so a routine event may race.
        | BaseMoved
        /// Closing keywords naming issues are present; they must be a declared fulfilment, which is not decidable here.
        | ClosingLinkPresent of count: int

    type Gap = { Input: Input; Kind: GapKind }

    val gapCode: gap: Gap -> string

    type Observed<'a> =
        | Known of 'a
        | NotObserved of GapKind

    type Verdict =
        | SupportedForNextStage of evidence: string
        | Unsupported of reason: Reason * supplier: Supplier
        | Unknown of gap: Gap

    type Finding =
        { Check: Check
          Verdict: Verdict
          /// Public subjects only: issue references, Project item ids or bare counts. Never private content.
          Subjects: string list }

    type Overall =
        | AllSupported
        | AnyUnsupported
        | AnyUnknown

    type Report =
        { Route: Route
          Stage: Stage
          Findings: Finding list
          Overall: Overall }

    /// 0 for all supported, 10 for any unsupported, 11 for any unknown (and none unsupported). Distinct from
    /// the refusal code 2. Unsupported dominates unknown. An empty report is unknown, never supported.
    val exitCode: overall: Overall -> int

    val overallOf: findings: Finding list -> Overall

    type FactsFreshness =
        /// Read in this run; plan-blob existence on main "now" can be asserted.
        | Live
        /// Replayed from a file; it can be used for the planner's joins but not for "now".
        | Supplied

    /// How the protected main revision was obtained.
    type MainFreshness =
        /// Read from the remote in this run.
        | LiveMain
        /// Asserted by the operator (`--main-revision`).
        | AssertedMain
        /// The checkout's local tracking ref; it may be stale.
        | LocalTrackingRef

    type EnrollmentInputs =
        { Request: V2Enrollment.Request
          Facts: Observed<V2Enrollment.NativeFacts * FactsFreshness>
          /// The source revision the running constructor was built from.
          ConstructorRevision: Observed<string>
          /// The intended protected main revision.
          ProtectedMain: Observed<string>
          /// How `ProtectedMain` was obtained. A differing revision from a local tracking ref is only `Unknown`.
          ProtectedMainFreshness: MainFreshness
          PredecessorPrivate: Observed<string>
          /// The public blob at the request's recorded predecessor merge.
          PredecessorPublic: Observed<string>
          RuntimeBinding: Observed<string>
          /// BindRuntime's own inputs: the successor private binding, the merge that delivered it, and the
          /// public blob at that merge.
          SuccessorPrivate: Observed<string>
          SuccessorMergeSha: Observed<string>
          SuccessorPublic: Observed<string>
          InstalledReaderSha256: Observed<string>
          /// Artifact shas of installed readers whose plan grammar is known to equal the source grammar.
          KnownInstalledReaders: Set<string>
          /// FSharp.Core `contentHash` locked in the constructor's lock file.
          LockedFSharpCoreHash: Observed<string>
          /// FSharp.Core `contentHash` recorded in the package cache's `.nupkg.metadata`.
          CachedFSharpCoreHash: Observed<string>
          /// Smallest remaining bytes across the cgroup chain; `Known None` means no limit at all.
          MemoryHeadroomBytes: Observed<int64 option>
          /// Host `MemAvailable`, consulted whenever no cgroup limit bounds the process.
          HostMemAvailableBytes: Observed<int64>
          MinimumHeadroomBytes: int64
          /// Bound on the successor payload, in bytes. Production: `payloadLimitBytes`.
          PayloadLimitBytes: int
          /// Bound on the successor private binding document, in characters. Production: `bindingLimitBytes`.
          BindingLimitBytes: int
          ProjectItems: Observed<V2NativeResolve.ProjectItem list>
          /// Repositories whose plan-document 404 may be invisibility rather than absence.
          InvisibleRepositories: Set<string>
          Now: DateTimeOffset
          ChurnWindow: TimeSpan }

    /// Evaluate every registered check for the route and stage. Total: never throws.
    val run: route: Route -> stage: Stage -> inputs: EnrollmentInputs -> Report

    /// Same as `run` with the planner's grammar replaced, for causal controls only.
    val runWith: seams: V2Enrollment.Seams -> route: Route -> stage: Stage -> inputs: EnrollmentInputs -> Report

    /// The routine-delivery policy fields the route reads (`.fsgg/routine-development.json`).
    type RoutinePolicy =
        { BranchPrefix: string
          AllowedOperations: string list
          ProtectedOperations: string list }

    /// The pull request, or the candidate about to become one.
    type RoutineCandidate =
        { BaseRef: string
          /// The base sha the pull request records; `None` before it is opened.
          BaseSha: string option
          HeadRef: string
          HeadSha: string
          Title: string
          Body: string }

    type RoutineInputs =
        { DefaultBranch: string
          Candidate: Observed<RoutineCandidate>
          Policy: Observed<RoutinePolicy>
          /// Live head of the default branch.
          LiveBaseSha: Observed<string>
          /// Open pull requests whose head branch is the candidate's.
          OpenPullRequests: Observed<int> }

    /// Result of the maintained coverage gate (`scripts/check-publication-writer-coverage.py --local`).
    type CoverageAuditResult =
        | Clean
        | Unmet of count: int

    type PublicationInputs =
        { Repository: string
          WorkflowPath: string
          Workflow: Observed<string>
          /// Text of `registry/publication-writers.json`.
          Registry: Observed<string>
          CoverageAudit: Observed<CoverageAuditResult> }

    /// The explicit `Unknown(Registry/NotRegistered)` report for a route and stage that do not belong together.
    val unregisteredReport: route: Route -> stage: Stage -> Report

    /// Evaluate the routine-delivery route for `OpenPullRequest` or `MergePullRequest`. Total.
    val runRoutine: stage: Stage -> inputs: RoutineInputs -> Report

    /// Evaluate the publication-writer route for `RegisterWriter` or `RunWriter`. Total. The coverage gate
    /// stays authoritative; this reports its result and the registration facts that decide the next stage.
    val runPublication: stage: Stage -> inputs: PublicationInputs -> Report

    /// Smallest headroom over a cgroup chain of (`memory.max`, `memory.current`) file texts. A limit of
    /// `max` is unlimited; anything unparseable makes the whole observation unreadable.
    val headroomOf: levels: (string * string) list -> Observed<int64 option>

    /// The `contentHash` of a `packages.lock.json` entry or `.nupkg.metadata` document for the package.
    val lockedContentHash: packageName: string -> lockJson: string -> Observed<string>

    /// The `contentHash` member of a `.nupkg.metadata` document.
    val metadataContentHash: metadataJson: string -> Observed<string>

    /// Closed `fsgg.board-v2-preflight/1` document. Deterministic.
    val toJson: report: Report -> string
