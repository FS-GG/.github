namespace FS.GG.Coord.GitHub

module V2Preflight =
    open System
    open System.Text
    open System.Text.Json
    open System.Text.Json.Nodes
    open V2NativeResolve

    let payloadLimitBytes = 131072
    let bindingLimitBytes = 262144

    type Route =
        | ProgrammeEnrollmentSuccessor
        | RoutineDelivery
        | PublicationWriterRegistration

    type Stage =
        | PlanSuccessor
        | PrepareSuccessor
        | BindRuntime
        | OpenPullRequest
        | MergePullRequest
        | RegisterWriter
        | RunWriter

    let routes = [ ProgrammeEnrollmentSuccessor; RoutineDelivery; PublicationWriterRegistration ]
    let stages = [ PlanSuccessor; PrepareSuccessor; BindRuntime; OpenPullRequest; MergePullRequest; RegisterWriter; RunWriter ]

    let routeCode route =
        match route with
        | ProgrammeEnrollmentSuccessor -> "ProgrammeEnrollmentSuccessor"
        | RoutineDelivery -> "RoutineDelivery"
        | PublicationWriterRegistration -> "PublicationWriterRegistration"

    let stageCode stage =
        match stage with
        | PlanSuccessor -> "PlanSuccessor"
        | PrepareSuccessor -> "PrepareSuccessor"
        | BindRuntime -> "BindRuntime"
        | OpenPullRequest -> "OpenPullRequest"
        | MergePullRequest -> "MergePullRequest"
        | RegisterWriter -> "RegisterWriter"
        | RunWriter -> "RunWriter"

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
        | SuccessorIntegrity
        | SuccessorExtendsPredecessor
        | BaseIsDefaultBranch
        | RoutineBranch
        | MarkerSingle
        | MarkerHeadBound
        | MarkerOperationAllowed
        | ClosingLinkDeclared
        | PullRequestOpen
        | ChangesPresent
        | SinglePullRequest
        | BaseCurrent
        | WorkflowReadable
        | WorkflowPublishes
        | RegistryReadable
        | NotYetRegistered
        | WriterRegistered
        | CoverageAudit

    let checkCode check =
        match check with
        | RouteRegistry -> "RouteRegistry"
        | ConstructorIdentity -> "ConstructorIdentity"
        | PredecessorIntegrity -> "PredecessorIntegrity"
        | RuntimeBindingIntegrity -> "RuntimeBindingIntegrity"
        | FactsComplete -> "FactsComplete"
        | IssueKind -> "IssueKind"
        | IssueIdentity -> "IssueIdentity"
        | ProjectMembership -> "ProjectMembership"
        | RepositoryInBinding -> "RepositoryInBinding"
        | NotAlreadyEnrolled -> "NotAlreadyEnrolled"
        | RoadmapConflict -> "RoadmapConflict"
        | PlanGrammar -> "PlanGrammar"
        | PlanBlob -> "PlanBlob"
        | RowCap -> "RowCap"
        | PayloadBound -> "PayloadBound"
        | TemplateBound -> "TemplateBound"
        | ToolchainLock -> "ToolchainLock"
        | MemoryHeadroom -> "MemoryHeadroom"
        | ProjectAutoAdd -> "ProjectAutoAdd"
        | AdjacencyChurn -> "AdjacencyChurn"
        | SuccessorIntegrity -> "SuccessorIntegrity"
        | SuccessorExtendsPredecessor -> "SuccessorExtendsPredecessor"
        | BaseIsDefaultBranch -> "BaseIsDefaultBranch"
        | RoutineBranch -> "RoutineBranch"
        | MarkerSingle -> "MarkerSingle"
        | MarkerHeadBound -> "MarkerHeadBound"
        | MarkerOperationAllowed -> "MarkerOperationAllowed"
        | ClosingLinkDeclared -> "ClosingLinkDeclared"
        | PullRequestOpen -> "PullRequestOpen"
        | ChangesPresent -> "ChangesPresent"
        | SinglePullRequest -> "SinglePullRequest"
        | BaseCurrent -> "BaseCurrent"
        | WorkflowReadable -> "WorkflowReadable"
        | WorkflowPublishes -> "WorkflowPublishes"
        | RegistryReadable -> "RegistryReadable"
        | NotYetRegistered -> "NotYetRegistered"
        | WriterRegistered -> "WriterRegistered"
        | CoverageAudit -> "CoverageAudit"

    // The next stage is the one about to run. Churn matters only where the installed caller is next;
    // bare unenrolled memberships are reported (as Unknown, their effect being unproven) from the first stage.
    let private planChecks =
        [ ConstructorIdentity; PredecessorIntegrity; RuntimeBindingIntegrity; FactsComplete; IssueKind; IssueIdentity
          ProjectMembership; RepositoryInBinding; NotAlreadyEnrolled; RoadmapConflict; PlanGrammar; PlanBlob; RowCap
          PayloadBound; TemplateBound; ToolchainLock; MemoryHeadroom; ProjectAutoAdd ]

    // BindRuntime acts on the successor: its private binding, the merge that delivered it and the public blob at
    // that merge. The predecessor appears only as the thing the successor must extend.
    let private bindChecks =
        [ ConstructorIdentity; SuccessorIntegrity; SuccessorExtendsPredecessor; PlanGrammar; PlanBlob; ToolchainLock
          MemoryHeadroom; ProjectAutoAdd; AdjacencyChurn ]

    // A candidate that is not yet a pull request has no base ref of its own to check, so `BaseIsDefaultBranch` and
    // `PullRequestOpen` are merge-stage checks; at open they would be tautologies.
    let private routineOpenChecks =
        [ RoutineBranch; MarkerSingle; MarkerHeadBound; MarkerOperationAllowed; ChangesPresent; ClosingLinkDeclared; SinglePullRequest ]

    let private routineMergeChecks =
        [ BaseIsDefaultBranch; PullRequestOpen; RoutineBranch; MarkerSingle; MarkerHeadBound; MarkerOperationAllowed; ChangesPresent
          ClosingLinkDeclared; SinglePullRequest; BaseCurrent ]

    let private registerChecks = [ WorkflowReadable; WorkflowPublishes; RegistryReadable; NotYetRegistered ]
    let private runWriterChecks = [ RegistryReadable; WriterRegistered; CoverageAudit ]

    let checksFor route stage =
        match route, stage with
        | ProgrammeEnrollmentSuccessor, (PlanSuccessor | PrepareSuccessor) -> planChecks
        | ProgrammeEnrollmentSuccessor, BindRuntime -> bindChecks
        | RoutineDelivery, OpenPullRequest -> routineOpenChecks
        | RoutineDelivery, MergePullRequest -> routineMergeChecks
        | PublicationWriterRegistration, RegisterWriter -> registerChecks
        | PublicationWriterRegistration, RunWriter -> runWriterChecks
        | _ -> []

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

    let supplierCode supplier =
        match supplier with
        | Root -> "Root"
        | ConstructorBuild -> "ConstructorBuild"
        | PredecessorCustody -> "PredecessorCustody"
        | PlanAuthor -> "PlanAuthor"
        | RequestAuthor -> "RequestAuthor"
        | ProtocolRelease -> "ProtocolRelease"
        | ToolchainCache -> "ToolchainCache"
        | HostCapacity -> "HostCapacity"
        | SuccessorCustody -> "SuccessorCustody"
        | PullRequestOwner -> "PullRequestOwner"
        | WriterRegistry -> "WriterRegistry"

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
        | PullRequestNotOpen
        | NoChangedPaths
        | ClosingKeywordUndeclared

    let reasonCode reason =
        match reason with
        | ConstructorDiffersFromMain -> "ConstructorDiffersFromMain"
        | PredecessorCommitmentMismatch -> "PredecessorCommitmentMismatch"
        | PredecessorNotSpliceable -> "PredecessorNotSpliceable"
        | RuntimeBindingDiffers -> "RuntimeBindingDiffers"
        | DuplicateRequestIssue -> "DuplicateRequestIssue"
        | IssueIsPullRequest -> "IssueIsPullRequest"
        | NodeIdentityMismatch -> "NodeIdentityMismatch"
        | NotProjectMember -> "NotProjectMember"
        | RepositoryOutsideBinding -> "RepositoryOutsideBinding"
        | AlreadyEnrolled -> "AlreadyEnrolled"
        | ConflictingHumanRoadmap -> "ConflictingHumanRoadmap"
        | PlanNotAdmitted -> "PlanNotAdmitted"
        | PlanBlobAbsentOnMain -> "PlanBlobAbsentOnMain"
        | RowCapExceeded -> "RowCapExceeded"
        | PayloadOverBound -> "PayloadOverBound"
        | BindingOverBound -> "BindingOverBound"
        | LockedHashDiffersFromCache -> "LockedHashDiffersFromCache"
        | InsufficientMemoryHeadroom -> "InsufficientMemoryHeadroom"
        | SuccessorCommitmentMismatch -> "SuccessorCommitmentMismatch"
        | SuccessorDoesNotExtendPredecessor -> "SuccessorDoesNotExtendPredecessor"
        | PullRequestBaseNotDefault -> "PullRequestBaseNotDefault"
        | BranchNotRoutine -> "BranchNotRoutine"
        | MarkerMissingOrAmbiguous -> "MarkerMissingOrAmbiguous"
        | MarkerHeadDiffers -> "MarkerHeadDiffers"
        | OperationNotAllowed -> "OperationNotAllowed"
        | PullRequestAlreadyOpen -> "PullRequestAlreadyOpen"
        | PullRequestNotSingle -> "PullRequestNotSingle"
        | WorkflowHasNoPublicationEffect -> "WorkflowHasNoPublicationEffect"
        | WriterAlreadyRegistered -> "WriterAlreadyRegistered"
        | WriterNotRegistered -> "WriterNotRegistered"
        | CoverageUnmet -> "CoverageUnmet"
        | PullRequestNotOpen -> "PullRequestNotOpen"
        | NoChangedPaths -> "NoChangedPaths"
        | ClosingKeywordUndeclared -> "ClosingKeywordUndeclared"

    let private supplierOf reason =
        match reason with
        | ConstructorDiffersFromMain -> ConstructorBuild
        | PredecessorCommitmentMismatch | PredecessorNotSpliceable | RuntimeBindingDiffers -> PredecessorCustody
        | DuplicateRequestIssue | IssueIsPullRequest | NodeIdentityMismatch | RepositoryOutsideBinding | AlreadyEnrolled -> RequestAuthor
        | NotProjectMember | ConflictingHumanRoadmap -> Root
        | PlanNotAdmitted | PlanBlobAbsentOnMain -> PlanAuthor
        | RowCapExceeded | PayloadOverBound | BindingOverBound -> ProtocolRelease
        | LockedHashDiffersFromCache -> ToolchainCache
        | InsufficientMemoryHeadroom -> HostCapacity
        | SuccessorCommitmentMismatch | SuccessorDoesNotExtendPredecessor -> SuccessorCustody
        | PullRequestBaseNotDefault | BranchNotRoutine | MarkerMissingOrAmbiguous | MarkerHeadDiffers | PullRequestAlreadyOpen | PullRequestNotSingle -> PullRequestOwner
        | OperationNotAllowed -> Root
        | WorkflowHasNoPublicationEffect | WriterAlreadyRegistered | WriterNotRegistered | CoverageUnmet -> WriterRegistry
        | PullRequestNotOpen | NoChangedPaths | ClosingKeywordUndeclared -> PullRequestOwner

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
        | RoutineGate
        | ClosingGate
        | WorkflowText
        | WriterRegistryText
        | CoverageGate
        | Planner

    type GapKind =
        | NotSupplied
        | Unreadable
        | Incomplete
        | NotLive
        | NotInKnownTable
        | NotEvaluated
        | ChurnObserved of count: int
        | BareMembershipsObserved of count: int
        | NotRegistered
        | ReadFailed
        | NotVisible
        | BaseMoved
        | ClosingLinkPresent of count: int
        | GateModified

    type Gap = { Input: Input; Kind: GapKind }

    let private inputCode input =
        match input with
        | SourceRevision -> "SourceRevision"
        | ProtectedMain -> "ProtectedMain"
        | PredecessorPrivate -> "PredecessorPrivate"
        | PredecessorPublic -> "PredecessorPublic"
        | RuntimeBinding -> "RuntimeBinding"
        | NativeFacts -> "NativeFacts"
        | ProjectItems -> "ProjectItems"
        | InstalledReader -> "InstalledReader"
        | KnownReaderTable -> "KnownReaderTable"
        | LockedHash -> "LockedHash"
        | CachedHash -> "CachedHash"
        | CgroupMemory -> "CgroupMemory"
        | HostMemory -> "HostMemory"
        | PlanBlobFact -> "PlanBlob"
        | SuccessorPrivate -> "SuccessorPrivate"
        | SuccessorMerge -> "SuccessorMerge"
        | SuccessorPublic -> "SuccessorPublic"
        | PullRequestFact -> "PullRequest"
        | OpenPullRequestFact -> "OpenPullRequests"
        | BaseHead -> "BaseHead"
        | RoutineGate -> "RoutineGate"
        | ClosingGate -> "ClosingGate"
        | WorkflowText -> "Workflow"
        | WriterRegistryText -> "WriterRegistry"
        | CoverageGate -> "CoverageAudit"
        | Registry -> "Registry"
        | Planner -> "Planner"

    let private kindCode kind =
        match kind with
        | NotSupplied -> "NotSupplied"
        | Unreadable -> "Unreadable"
        | Incomplete -> "Incomplete"
        | NotLive -> "NotLive"
        | NotInKnownTable -> "NotInKnownTable"
        | NotEvaluated -> "NotEvaluated"
        | ChurnObserved count -> "ChurnObserved:" + string count
        | BareMembershipsObserved count -> "BareMembershipsObserved:" + string count
        | NotRegistered -> "NotRegistered"
        | ReadFailed -> "ReadFailed"
        | NotVisible -> "NotVisible"
        | BaseMoved -> "BaseMoved"
        | ClosingLinkPresent count -> "ClosingLinkPresent:" + string count
        | GateModified -> "GateModified"

    let gapCode (gap: Gap) = inputCode gap.Input + "/" + kindCode gap.Kind

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

    let overallOf (findings: Finding list) =
        if findings.IsEmpty then AnyUnknown
        elif findings |> List.exists (fun finding -> match finding.Verdict with Unsupported _ -> true | _ -> false) then AnyUnsupported
        elif findings |> List.exists (fun finding -> match finding.Verdict with Unknown _ -> true | _ -> false) then AnyUnknown
        else AllSupported

    let exitCode overall =
        match overall with
        | AllSupported -> 0
        | AnyUnsupported -> 10
        | AnyUnknown -> 11

    type FactsFreshness =
        | Live
        | Supplied

    type MainFreshness =
        | LiveMain
        | AssertedMain
        | LocalTrackingRef

    type EnrollmentInputs =
        { Request: V2Enrollment.Request
          Facts: Observed<V2Enrollment.NativeFacts * FactsFreshness>
          ConstructorRevision: Observed<string>
          ProtectedMain: Observed<string>
          ProtectedMainFreshness: MainFreshness
          PredecessorPrivate: Observed<string>
          PredecessorPublic: Observed<string>
          RuntimeBinding: Observed<string>
          SuccessorPrivate: Observed<string>
          SuccessorMergeSha: Observed<string>
          SuccessorPublic: Observed<string>
          InstalledReaderSha256: Observed<string>
          KnownInstalledReaders: Set<string>
          KnownLiftedCapReaders: Set<string>
          KnownCap500Readers: Set<string>
          LockedFSharpCoreHash: Observed<string>
          CachedFSharpCoreHash: Observed<string>
          MemoryHeadroomBytes: Observed<int64 option>
          HostMemAvailableBytes: Observed<int64>
          MinimumHeadroomBytes: int64
          PayloadLimitBytes: int
          BindingLimitBytes: int
          ProjectItems: Observed<ProjectItem list>
          InvisibleRepositories: Set<string>
          Now: DateTimeOffset
          ChurnWindow: TimeSpan }

    type RoutineCandidate =
        { BaseRef: string
          BaseSha: string option
          HeadRef: string
          HeadSha: string
          State: string option }

    type RoutineGateResult =
        | Admitted
        | GateFinding of kind: string

    type ClosingGateResult =
        | NoClosing
        | DeclaredClosing of references: string list
        | UndeclaredClosing

    type RoutineInputs =
        { DefaultBranch: string
          Candidate: Observed<RoutineCandidate>
          LiveBaseSha: Observed<string>
          OpenPullRequests: Observed<int>
          RoutineGate: Observed<RoutineGateResult>
          ClosingGate: Observed<ClosingGateResult> }

    type CoverageAuditResult =
        | Clean
        | Unmet of count: int

    type PublicationInputs =
        { Repository: string
          WorkflowPath: string
          Workflow: Observed<string>
          Registry: Observed<string>
          CoverageAudit: Observed<CoverageAuditResult> }

    // ---- verdict constructors ----

    let private supported check evidence = { Check = check; Verdict = SupportedForNextStage evidence; Subjects = [] }
    let private unsupported check reason subjects = { Check = check; Verdict = Unsupported(reason, supplierOf reason); Subjects = subjects }
    let private unknown check input kind subjects = { Check = check; Verdict = Unknown { Input = input; Kind = kind }; Subjects = subjects }

    let private nameCap = 50

    let private capped (names: string list) =
        let distinct = names |> List.distinct
        if distinct.Length <= nameCap then distinct
        else (distinct |> List.take nameCap) @ [ "+" + string (distinct.Length - nameCap) + " more" ]

    // ---- shared context ----

    type private Context =
        { Inputs: EnrollmentInputs
          Seams: V2Enrollment.Seams
          Stage: Stage
          Binding: V2Projection.Binding option
          // The binding whose rows count as enrolled at this stage: the successor's at BindRuntime.
          StageBinding: V2Projection.Binding option
          Analysis: Result<V2Enrollment.Analysis, V2Enrollment.Refusal> option
          FactsKnown: V2Enrollment.NativeFacts option }

    let private emptyFacts : V2Enrollment.NativeFacts = { ProjectId = ""; Issues = []; PlanBlobs = [] }

    let private decodeBindingOf (observed: Observed<string>) =
        match observed with
        | Known json -> (match V2Projection.decodeBinding json with Ok value -> Some value | Error _ -> None)
        | NotObserved _ -> None

    let private context (seams: V2Enrollment.Seams) (stage: Stage) (inputs: EnrollmentInputs) : Context =
        let binding = decodeBindingOf inputs.PredecessorPrivate
        let stageBinding = if stage = BindRuntime then decodeBindingOf inputs.SuccessorPrivate else binding
        let facts = match inputs.Facts with Known(facts, _) -> Some facts | NotObserved _ -> None
        let analysis =
            match inputs.PredecessorPrivate, inputs.PredecessorPublic with
            | Known privateJson, Known publicJson ->
                // Without facts only the facts-independent findings are consulted; the rest report their own gap.
                Some(V2Enrollment.analyzeWith seams privateJson publicJson inputs.Request (defaultArg facts emptyFacts))
            | _ -> None
        { Inputs = inputs; Seams = seams; Stage = stage; Binding = binding; StageBinding = stageBinding; Analysis = analysis; FactsKnown = facts }

    let private predecessorGap (ctx: Context) : (Input * GapKind) option =
        match ctx.Inputs.PredecessorPrivate, ctx.Inputs.PredecessorPublic with
        | NotObserved kind, _ -> Some(PredecessorPrivate, kind)
        | _, NotObserved kind -> Some(PredecessorPublic, kind)
        | _ -> None

    let private factsGap (ctx: Context) : GapKind option =
        match ctx.Inputs.Facts with
        | NotObserved kind -> Some kind
        | Known _ -> None

    let private subjectsOf (hits: V2Enrollment.Finding list) = hits |> List.choose _.Issue |> capped

    let private hitsOf (analysis: V2Enrollment.Analysis) (refusal: V2Enrollment.Refusal) =
        analysis.Findings |> List.filter (fun finding -> finding.Refusal = refusal)

    // A check decided purely by whether the planner reported a given refusal. `needsFacts` checks report
    // their own gap when the facts were not observed.
    let private fromAnalysis (ctx: Context) check (needsFacts: bool) (refusal: V2Enrollment.Refusal) (reason: Reason) (evidence: string) : Finding =
        match predecessorGap ctx, ctx.Analysis with
        | Some(input, kind), _ -> unknown check input kind []
        | None, Some(Error _) -> unknown check PredecessorPrivate NotEvaluated []
        | None, None -> unknown check PredecessorPrivate NotEvaluated []
        | None, Some(Ok analysis) ->
            match (if needsFacts then factsGap ctx else None) with
            | Some kind -> unknown check NativeFacts kind []
            | None ->
                match hitsOf analysis refusal with
                | [] -> supported check evidence
                | hits -> unsupported check reason (subjectsOf hits)

    // ---- the checks ----

    let private constructorIdentity (ctx: Context) =
        match ctx.Inputs.ConstructorRevision, ctx.Inputs.ProtectedMain with
        | NotObserved kind, _ -> unknown ConstructorIdentity SourceRevision kind []
        | _, NotObserved kind -> unknown ConstructorIdentity ProtectedMain kind []
        | Known built, Known main ->
            let source =
                match ctx.Inputs.ProtectedMainFreshness with
                | LiveMain -> "live remote head"
                | AssertedMain -> "revision asserted by the operator"
                | LocalTrackingRef -> "local tracking ref; it may be stale"
            match ctx.Inputs.ProtectedMainFreshness with
            // A stale checkout can equal a stale tracking ref, so neither a match nor a difference is evidence offline.
            | LocalTrackingRef -> unknown ConstructorIdentity ProtectedMain NotLive []
            | LiveMain | AssertedMain ->
                if built = main then supported ConstructorIdentity ("constructor source revision equals the intended protected main (" + source + ")")
                else unsupported ConstructorIdentity ConstructorDiffersFromMain []

    let private predecessorIntegrity (ctx: Context) =
        match predecessorGap ctx, ctx.Analysis with
        | Some(input, kind), _ -> unknown PredecessorIntegrity input kind []
        | None, Some(Ok analysis) ->
            if (hitsOf analysis V2Enrollment.PrefixNotPreserved) |> List.exists (fun finding -> finding.Issue.IsNone) then
                unsupported PredecessorIntegrity PredecessorNotSpliceable []
            else supported PredecessorIntegrity ("private pair authenticates against the public blob; rows=" + string analysis.PredecessorRows)
        | None, _ -> unsupported PredecessorIntegrity PredecessorCommitmentMismatch []

    let private stripPopulationRevision (json: string) =
        let node = JsonNode.Parse json
        node.AsObject().Remove "populationRevision" |> ignore
        node

    let private runtimeBindingIntegrity (ctx: Context) =
        match ctx.Inputs.PredecessorPrivate, ctx.Inputs.PredecessorPublic, ctx.Inputs.RuntimeBinding with
        | NotObserved kind, _, _ -> unknown RuntimeBindingIntegrity PredecessorPrivate kind []
        | _, NotObserved kind, _ -> unknown RuntimeBindingIntegrity PredecessorPublic kind []
        | _, _, NotObserved kind -> unknown RuntimeBindingIntegrity RuntimeBinding kind []
        | Known privateJson, Known publicJson, Known runtimeJson ->
            let consistent =
                try
                    match V2Projection.decodeBinding privateJson, V2Projection.decodeBinding runtimeJson with
                    | Ok pair, Ok runtime ->
                        runtime.PopulationRevision = ctx.Inputs.Request.PredecessorMergeSha
                        && JsonNode.DeepEquals(stripPopulationRevision privateJson, stripPopulationRevision runtimeJson)
                        && (V2Projection.authenticateProgrammeEnrollment runtime publicJson |> Result.isOk)
                        && pair.ProjectId = runtime.ProjectId
                    | _ -> false
                with _ -> false
            if consistent then supported RuntimeBindingIntegrity "runtime binding differs from the private pair only in populationRevision, which is the recorded merge"
            else unsupported RuntimeBindingIntegrity RuntimeBindingDiffers []

    let private factsComplete (ctx: Context) =
        match predecessorGap ctx, ctx.Analysis, factsGap ctx with
        | Some(input, kind), _, _ -> unknown FactsComplete input kind []
        | _, _, Some kind -> unknown FactsComplete NativeFacts kind []
        | None, Some(Ok analysis), None ->
            match hitsOf analysis V2Enrollment.PartialVisibility with
            | [] -> supported FactsComplete "every request issue has a complete native fact"
            | hits -> unknown FactsComplete NativeFacts Incomplete (subjectsOf hits)
        | _ -> unknown FactsComplete PredecessorPrivate NotEvaluated []

    let private issueIdentity (ctx: Context) =
        let identity = fromAnalysis ctx IssueIdentity true V2Enrollment.NodeIdMismatch NodeIdentityMismatch "every node id is distinct, an issue node and consistent with the predecessor"
        // A duplicated request issue is decidable without facts and takes precedence.
        match ctx.Analysis with
        | Some(Ok analysis) ->
            match hitsOf analysis V2Enrollment.DuplicateIssue with
            | [] -> identity
            | hits -> unsupported IssueIdentity DuplicateRequestIssue (subjectsOf hits)
        | _ -> identity

    let private installedGrammarGate (ctx: Context) (evidenceKind: string) =
        // The source grammar admits the references; whether the installed reader's grammar is the same is known
        // only for artifacts in the table.
        match ctx.Inputs.InstalledReaderSha256 with
        | NotObserved kind -> unknown PlanGrammar InstalledReader kind []
        | Known sha ->
            if ctx.Inputs.KnownInstalledReaders.IsEmpty then unknown PlanGrammar KnownReaderTable NotSupplied []
            elif ctx.Inputs.KnownInstalledReaders.Contains sha then supported PlanGrammar ("source grammar admits " + evidenceKind + " and the installed reader is in the known table")
            else unknown PlanGrammar InstalledReader NotInKnownTable []

    let private planningPlanGrammar (ctx: Context) =
        let protectedDocs = ctx.Inputs.Request.Issues |> List.filter (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc _ -> true | _ -> false)
        let source = fromAnalysis ctx PlanGrammar false V2Enrollment.PlanNotAdmitted PlanNotAdmitted "every plan reference is admitted by the source plan grammar"
        match source.Verdict with
        | SupportedForNextStage _ when not protectedDocs.IsEmpty -> installedGrammarGate ctx "every reference"
        | _ -> source

    // At BindRuntime the references that matter are the rows the successor binding actually carries and the
    // predecessor did not: read from the successor's own payload, not from the request.
    let private appendedRows (ctx: Context) : Result<(string * string * string) list, Input * GapKind> =
        match ctx.StageBinding, ctx.Binding with
        | None, _ -> Error(SuccessorPrivate, Unreadable)
        | _, None -> Error(PredecessorPrivate, Unreadable)
        | Some successor, Some predecessor ->
            match V2Projection.programmeRows successor with
            | Error _ -> Error(SuccessorPrivate, Unreadable)
            | Ok rows -> Ok(rows |> List.filter (fun (_, node, _) -> not (predecessor.SelectedIssues.ContainsKey node)))

    let private admittedDoc (ctx: Context) (repositories: Set<string>) (roadmap: string) =
        match ctx.Seams.Grammar repositories roadmap with
        | Ok(repository, reference, path) -> Some(repository, reference, path)
        | Error _ -> None

    let private bindingPlanGrammar (ctx: Context) =
        match appendedRows ctx, ctx.StageBinding with
        | Error(input, kind), _ -> unknown PlanGrammar input kind []
        | Ok rows, Some successor ->
            let isIssueUrl (roadmap: string) = roadmap = V2ProjectionSource.programmePlanIssueUrl
            let refused = rows |> List.filter (fun (_, _, roadmap) -> not (isIssueUrl roadmap) && (admittedDoc ctx successor.Repositories roadmap).IsNone)
            if not refused.IsEmpty then unsupported PlanGrammar PlanNotAdmitted (refused |> List.map (fun (issue, _, _) -> issue) |> capped)
            elif rows |> List.forall (fun (_, _, roadmap) -> isIssueUrl roadmap) then supported PlanGrammar "every appended row names the programme plan issue"
            else installedGrammarGate ctx "every appended reference"
        | Ok _, None -> unknown PlanGrammar SuccessorPrivate Unreadable []

    let private planGrammar (ctx: Context) = if ctx.Stage = BindRuntime then bindingPlanGrammar ctx else planningPlanGrammar ctx

    let private planningPlanBlob (ctx: Context) =
        let docs = ctx.Inputs.Request.Issues |> List.filter (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc _ -> true | _ -> false)
        match predecessorGap ctx, ctx.Inputs.Facts with
        | _, NotObserved kind -> unknown PlanBlob NativeFacts kind []
        | _ when docs.IsEmpty -> supported PlanBlob "no protected document reference"
        | _, Known(_, Supplied) -> unknown PlanBlob NativeFacts NotLive []
        | _, Known(_, Live) ->
            let found = fromAnalysis ctx PlanBlob true V2Enrollment.PlanBlobAbsentOnMain PlanBlobAbsentOnMain "every protected document exists on main now"
            match found.Verdict with
            | Unsupported(PlanBlobAbsentOnMain, _) ->
                // A 404 in a repository the reader may not see is not an absence.
                let repositoryOf (reference: string) =
                    ctx.Inputs.Request.Issues
                    |> List.tryFind (fun issue -> String.Equals(issue.Ref, reference, StringComparison.OrdinalIgnoreCase))
                    |> Option.bind (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc(repository, _) -> Some repository | _ -> None)
                let hidden, visible =
                    found.Subjects |> List.partition (fun reference ->
                        match repositoryOf reference with
                        | Some repository -> ctx.Inputs.InvisibleRepositories.Contains repository
                        | None -> false)
                if visible.IsEmpty && not hidden.IsEmpty then unknown PlanBlob PlanBlobFact NotVisible hidden
                else { found with Subjects = visible }
            | _ -> found

    let private bindingPlanBlob (ctx: Context) =
        match appendedRows ctx, ctx.StageBinding, ctx.Inputs.Facts with
        | Error(input, kind), _, _ -> unknown PlanBlob input kind []
        | _, _, NotObserved kind -> unknown PlanBlob NativeFacts kind []
        | Ok rows, Some successor, Known(facts, freshness) ->
            let docs = rows |> List.choose (fun (issue, _, roadmap) -> admittedDoc ctx successor.Repositories roadmap |> Option.map (fun (repository, reference, path) -> issue, repository, reference, path))
            if docs.IsEmpty then supported PlanBlob "no appended protected document reference"
            elif freshness = Supplied then unknown PlanBlob NativeFacts NotLive []
            else
                // Only a main reference can be checked against the facts, which describe main now.
                let absent = ResizeArray<string>()
                let hidden = ResizeArray<string>()
                let unread = ResizeArray<string>()
                let pinned = ResizeArray<string>()
                for issue, repository, reference, path in docs do
                    match facts.PlanBlobs |> List.tryFind (fun blob -> blob.Repository = repository && blob.Path = path) with
                    // A sha-pinned reference is immutable and says nothing about main now; it is not checked here.
                    | _ when reference <> "main" -> pinned.Add issue
                    | None -> unread.Add issue
                    | Some blob when blob.OnMain -> ()
                    | Some _ -> if ctx.Inputs.InvisibleRepositories.Contains repository then hidden.Add issue else absent.Add issue
                if absent.Count > 0 then unsupported PlanBlob PlanBlobAbsentOnMain (List.ofSeq absent |> capped)
                elif hidden.Count > 0 then unknown PlanBlob PlanBlobFact NotVisible (List.ofSeq hidden |> capped)
                elif unread.Count > 0 then unknown PlanBlob NativeFacts Incomplete (List.ofSeq unread |> capped)
                elif pinned.Count > 0 then unknown PlanBlob PlanBlobFact NotEvaluated (List.ofSeq pinned |> capped)
                else supported PlanBlob "every appended main-branch protected document exists on main now"
        | Ok _, None, _ -> unknown PlanBlob SuccessorPrivate Unreadable []

    let private planBlob (ctx: Context) = if ctx.Stage = BindRuntime then bindingPlanBlob ctx else planningPlanBlob ctx

    let private rowCap (ctx: Context) =
        match predecessorGap ctx, ctx.Analysis with
        | Some(input, kind), _ -> unknown RowCap input kind []
        | None, Some(Ok analysis) ->
            let total = analysis.PredecessorRows + ctx.Inputs.Request.Issues.Length
            let counts = string analysis.PredecessorRows + "+" + string ctx.Inputs.Request.Issues.Length
            if total > V2Enrollment.rowCap then
                { unsupported RowCap RowCapExceeded [] with Subjects = [ counts + ">" + string V2Enrollment.rowCap ] }
            elif total <= V2Enrollment.legacyRowCap then
                supported RowCap (counts + "<=" + string V2Enrollment.legacyRowCap + ", readable by every reader of the line")
            else
                // Above the legacy ceiling the successor is readable only by a reader that carries a lifted one: up to 250
                // by a reader in either table, and from 251 to 500 only by a reader in the 500-row table (#5119).
                let within250 = total <= V2Enrollment.previousRowCap
                let table = if within250 then Set.union ctx.Inputs.KnownLiftedCapReaders ctx.Inputs.KnownCap500Readers else ctx.Inputs.KnownCap500Readers
                let ceiling = if within250 then V2Enrollment.previousRowCap else V2Enrollment.rowCap
                match ctx.Inputs.InstalledReaderSha256 with
                | NotObserved kind -> unknown RowCap InstalledReader kind []
                | Known sha ->
                    if table.IsEmpty then unknown RowCap KnownReaderTable NotSupplied []
                    elif table.Contains sha then
                        supported RowCap (counts + "<=" + string ceiling + " and the installed reader is known to carry that ceiling")
                    else unknown RowCap InstalledReader NotInKnownTable []
        | None, _ -> unknown RowCap PredecessorPrivate NotEvaluated []

    // Sizes are computed from the request, the facts and the predecessor, not from a built successor: a
    // successor that exceeds the installed bounds cannot be built at all, so building it would only ever
    // report that nothing could be measured. Every counted value is pattern-validated ASCII, so no escaping
    // changes a length.
    let private fragmentOverhead = 38 // {"issue":"","nodeId":"","roadmap":""} (37) plus the separating comma

    let private roadmapLength (issue: V2Enrollment.RequestIssue) =
        match issue.Plan with
        | V2Enrollment.ProgrammeIssue3008 -> V2ProjectionSource.programmePlanIssueUrl.Length
        | V2Enrollment.ProtectedDoc(repository, path) -> ("https://github.com/" + repository + "/blob/main/" + path).Length

    let private estimate (ctx: Context) =
        match predecessorGap ctx, ctx.Analysis with
        | Some(input, kind), _ -> Error(input, kind)
        | None, Some(Ok analysis) ->
            match ctx.Inputs.PredecessorPrivate with
            | Known privateJson ->
                let nodeLength (issue: V2Enrollment.RequestIssue) =
                    match ctx.FactsKnown with
                    | Some facts ->
                        facts.Issues |> List.tryFind (fun fact -> String.Equals(fact.Ref, issue.Ref, StringComparison.OrdinalIgnoreCase))
                        |> Option.map (fun fact -> fact.NodeId.Length) |> Option.defaultValue 200
                    | None -> 200
                let issues = ctx.Inputs.Request.Issues
                let added = issues |> List.sumBy (fun issue -> fragmentOverhead + issue.Ref.Length + nodeLength issue + roadmapLength issue)
                let payload = analysis.PredecessorPayloadBytes + added
                let selected = issues |> List.sumBy (fun issue -> issue.Ref.Length + nodeLength issue + 8)
                let base64 (bytes: int) = (bytes + 2) / 3 * 4
                let privateBinding = privateJson.Length - base64 analysis.PredecessorPayloadBytes + base64 payload + selected
                Ok(payload, privateBinding)
            | _ -> Error(PredecessorPrivate, Unreadable)
        | None, _ -> Error(Planner, NotEvaluated)

    let private payloadBound (ctx: Context) =
        match estimate ctx with
        | Error(input, kind) -> unknown PayloadBound input kind []
        | Ok(payload, _) ->
            let limit = ctx.Inputs.PayloadLimitBytes
            if payload > limit then { unsupported PayloadBound PayloadOverBound [] with Subjects = [ string payload + ">" + string limit ] }
            else supported PayloadBound (string payload + "<=" + string limit + " payload bytes")

    let private templateBound (ctx: Context) =
        match estimate ctx with
        | Error(input, kind) -> unknown TemplateBound input kind []
        | Ok(_, privateBinding) ->
            let limit = ctx.Inputs.BindingLimitBytes
            if privateBinding > limit then { unsupported TemplateBound BindingOverBound [] with Subjects = [ string privateBinding + ">" + string limit ] }
            else supported TemplateBound (string privateBinding + "<=" + string limit + " private binding characters")

    let private toolchainLock (ctx: Context) =
        match ctx.Inputs.LockedFSharpCoreHash, ctx.Inputs.CachedFSharpCoreHash with
        | NotObserved kind, _ -> unknown ToolchainLock LockedHash kind []
        | _, NotObserved kind -> unknown ToolchainLock CachedHash kind []
        | Known locked, Known cached ->
            if String.Equals(locked, cached, StringComparison.Ordinal) then supported ToolchainLock "locked FSharp.Core content hash equals the package cache"
            else unsupported ToolchainLock LockedHashDiffersFromCache []

    let private memoryHeadroom (ctx: Context) =
        // With no cgroup limit the host's MemAvailable is the bound; with one, the smaller of the two. An
        // unreadable cgroup stays Unknown whatever the host reports, because it may be the tighter bound.
        let effective =
            match ctx.Inputs.MemoryHeadroomBytes, ctx.Inputs.HostMemAvailableBytes with
            | NotObserved kind, _ -> Error(CgroupMemory, kind)
            | Known(Some limit), Known host -> Ok(min limit host)
            // A known cgroup limit does not bound the host: with MemAvailable unobserved the tighter bound is unknown.
            | Known(Some _), NotObserved kind -> Error(HostMemory, kind)
            | Known None, Known host -> Ok host
            | Known None, NotObserved kind -> Error(HostMemory, kind)
        match effective with
        | Error(input, kind) -> unknown MemoryHeadroom input kind []
        | Ok headroom ->
            let minimum = ctx.Inputs.MinimumHeadroomBytes
            if headroom < minimum then { unsupported MemoryHeadroom InsufficientMemoryHeadroom [] with Subjects = [ string headroom + "<" + string minimum ] }
            else supported MemoryHeadroom (string headroom + ">=" + string minimum + " bytes headroom")

    let private nameOf (item: ProjectItem) =
        match item.Content with
        | IssueContent(reference, _) | PullRequestContent(reference, _) -> reference
        | OtherContent kind -> kind + ":" + item.ItemId

    let private projectAutoAdd (ctx: Context) =
        match ctx.Inputs.ProjectItems, ctx.Inputs.PredecessorPrivate, ctx.Binding, ctx.StageBinding with
        | NotObserved kind, _, _, _ -> unknown ProjectAutoAdd ProjectItems kind []
        | _, NotObserved kind, _, _ -> unknown ProjectAutoAdd PredecessorPrivate kind []
        | _, _, None, _ -> unknown ProjectAutoAdd PredecessorPrivate Unreadable []
        | _, _, _, None -> unknown ProjectAutoAdd SuccessorPrivate Unreadable []
        | Known items, _, Some predecessor, Some staged ->
            // The inventory must contain every node the predecessor enrolled; otherwise it is not the whole
            // Project, and an absence of bare memberships in it proves nothing.
            let present = items |> List.choose (fun item -> match item.Content with IssueContent(_, node) -> Some node | _ -> None) |> Set.ofList
            let missing = predecessor.SelectedIssues |> Map.toList |> List.filter (fun (node, _) -> not (present.Contains node)) |> List.map snd
            if not missing.IsEmpty then unknown ProjectAutoAdd ProjectItems Incomplete (capped missing)
            else
                let enrolled (item: ProjectItem) =
                    match item.Content with
                    | IssueContent(_, node) -> staged.SelectedIssues.ContainsKey node
                    | _ -> false
                let unenrolled = items |> List.filter (enrolled >> not)
                let offenders = unenrolled |> List.filter (fun item -> not (item.HasStatus && item.HasTrack && item.HasWorkLevel))
                if offenders.IsEmpty then supported ProjectAutoAdd (string unenrolled.Length + " unenrolled item(s), each with Status, Track and Work level; every enrolled node is in the inventory")
                else
                    // Each offender is named with exactly which of the three fields it lacks, so a fully bare
                    // membership can be told from a legacy one that merely lacks Track and Work level.
                    let named (item: ProjectItem) =
                        let missingFields = [ (if item.HasStatus then None else Some "Status"); (if item.HasTrack then None else Some "Track"); (if item.HasWorkLevel then None else Some "Work level") ] |> List.choose id
                        nameOf item + "[" + String.Join(",", missingFields) + "]"
                    // Unknown, not Unsupported: that these break the installed caller is unproven (see the gap kind).
                    unknown ProjectAutoAdd ProjectItems (BareMembershipsObserved offenders.Length) (offenders |> List.map named |> capped)

    let private adjacencyChurn (ctx: Context) =
        match ctx.Inputs.ProjectItems with
        | NotObserved kind -> unknown AdjacencyChurn ProjectItems kind []
        | Known items ->
            let requested = ctx.Inputs.Request.Issues |> List.map (fun issue -> issue.Ref.ToLowerInvariant()) |> Set.ofList
            let since = ctx.Inputs.Now - ctx.Inputs.ChurnWindow
            let related (item: ProjectItem) =
                match item.Content with
                | IssueContent(reference, _) | PullRequestContent(reference, _) -> requested.Contains(reference.ToLowerInvariant())
                | OtherContent _ -> false
            let recent = items |> List.filter (fun item -> item.UpdatedAt >= since && not (related item))
            if recent.IsEmpty then supported AdjacencyChurn "no unrelated Project item updated within the window"
            else unknown AdjacencyChurn ProjectItems (ChurnObserved recent.Length) (recent |> List.map nameOf |> capped)

    // ---- BindRuntime acts on the successor ----

    let private hex40 (value: string) = Text.RegularExpressions.Regex.IsMatch(value, @"\A[0-9a-f]{40}\z")

    let private successorGap (ctx: Context) : (Input * GapKind) option =
        match ctx.Inputs.SuccessorPrivate, ctx.Inputs.SuccessorMergeSha, ctx.Inputs.SuccessorPublic with
        | NotObserved kind, _, _ -> Some(SuccessorPrivate, kind)
        | _, NotObserved kind, _ -> Some(SuccessorMerge, kind)
        | _, _, NotObserved kind -> Some(SuccessorPublic, kind)
        | Known _, Known merge, Known _ -> if hex40 merge then None else Some(SuccessorMerge, Unreadable)

    let private successorIntegrity (ctx: Context) =
        match successorGap ctx, ctx.Inputs.SuccessorPrivate, ctx.Inputs.SuccessorPublic with
        | Some(input, kind), _, _ -> unknown SuccessorIntegrity input kind []
        | None, Known privateJson, Known publicJson ->
            let authenticated =
                try
                    match V2Projection.decodeBinding privateJson with
                    | Ok binding -> V2Projection.isProgrammeBinding binding && (V2Projection.authenticateProgrammeEnrollment binding publicJson |> Result.isOk)
                    | Error _ -> false
                with _ -> false
            if authenticated then supported SuccessorIntegrity "successor private pair authenticates against the public blob at the merge"
            else unsupported SuccessorIntegrity SuccessorCommitmentMismatch []
        | None, _, _ -> unknown SuccessorIntegrity SuccessorPrivate NotEvaluated []

    let private successorExtendsPredecessor (ctx: Context) =
        match successorGap ctx, ctx.Inputs.PredecessorPrivate, ctx.Inputs.SuccessorPrivate, ctx.Inputs.SuccessorPublic with
        | Some(input, kind), _, _, _ -> unknown SuccessorExtendsPredecessor input kind []
        | None, NotObserved kind, _, _ -> unknown SuccessorExtendsPredecessor PredecessorPrivate kind []
        | None, Known before, Known after, Known publicJson ->
            let verdict = V2Enrollment.verifyPair before after publicJson
            if verdict.PrefixPreserved && verdict.HeaderPreserved && verdict.RecomputedCommitment && verdict.AppendedRows >= 1 then
                supported SuccessorExtendsPredecessor (string verdict.AppendedRows + " appended row(s); predecessor prefix and header preserved; commitment recomputed")
            else unsupported SuccessorExtendsPredecessor SuccessorDoesNotExtendPredecessor []
        | None, _, _, _ -> unknown SuccessorExtendsPredecessor SuccessorPrivate NotEvaluated []

    let private evaluate (ctx: Context) (check: Check) : Finding =
        match check with
        | RouteRegistry -> unknown RouteRegistry Registry NotRegistered []
        | ConstructorIdentity -> constructorIdentity ctx
        | PredecessorIntegrity -> predecessorIntegrity ctx
        | RuntimeBindingIntegrity -> runtimeBindingIntegrity ctx
        | FactsComplete -> factsComplete ctx
        | IssueKind -> fromAnalysis ctx IssueKind true V2Enrollment.IsPullRequest IssueIsPullRequest "every request issue is an issue, not a pull request"
        | IssueIdentity -> issueIdentity ctx
        | ProjectMembership -> fromAnalysis ctx ProjectMembership true V2Enrollment.NotProjectMember NotProjectMember "every request issue is a member of the bound Project"
        | RepositoryInBinding -> fromAnalysis ctx RepositoryInBinding false V2Enrollment.RepositoryOutsideBinding RepositoryOutsideBinding "every request repository is in the binding"
        | NotAlreadyEnrolled -> fromAnalysis ctx NotAlreadyEnrolled true V2Enrollment.AlreadyEnrolled AlreadyEnrolled "no request issue is already enrolled"
        | RoadmapConflict -> fromAnalysis ctx RoadmapConflict true V2Enrollment.ConflictingHumanValue ConflictingHumanRoadmap "no differing human Roadmap value"
        | PlanGrammar -> planGrammar ctx
        | PlanBlob -> planBlob ctx
        | RowCap -> rowCap ctx
        | PayloadBound -> payloadBound ctx
        | TemplateBound -> templateBound ctx
        | ToolchainLock -> toolchainLock ctx
        | MemoryHeadroom -> memoryHeadroom ctx
        | ProjectAutoAdd -> projectAutoAdd ctx
        | AdjacencyChurn -> adjacencyChurn ctx
        | SuccessorIntegrity -> successorIntegrity ctx
        | SuccessorExtendsPredecessor -> successorExtendsPredecessor ctx
        | _ -> unknown check Registry NotRegistered []

    let private reportOf route stage findings = { Route = route; Stage = stage; Findings = findings; Overall = overallOf findings }

    let private unregistered route stage = reportOf route stage [ unknown RouteRegistry Registry NotRegistered [] ]

    let unregisteredReport route stage = unregistered route stage

    let runWith (seams: V2Enrollment.Seams) (route: Route) (stage: Stage) (inputs: EnrollmentInputs) : Report =
        // This entry evaluates the enrollment route only; the other routes have their own inputs and runners.
        let checks = if route = ProgrammeEnrollmentSuccessor then checksFor route stage else []
        if checks.IsEmpty then unregistered route stage
        else
            let ctx = context seams stage inputs
            reportOf route stage (checks |> List.map (fun check -> try evaluate ctx check with _ -> unknown check Registry NotEvaluated []))

    let run route stage inputs = runWith V2Enrollment.defaultSeams route stage inputs

    // ---- routine delivery ----

    // The gate's own branch pattern (`ROUTINE_BRANCH_RE` in scripts/check-claim-generation.py).
    let private routineBranch = Text.RegularExpressions.Regex(@"\Aroutine/[a-z0-9][a-z0-9._-]*\z", Text.RegularExpressions.RegexOptions.CultureInvariant)

    // The routine gate evaluates in this order and stops at the first finding, so a check is decided only if the
    // gate got that far: earlier checks passed, later ones were not evaluated.
    let private gatePhase (kind: string) =
        match kind with
        | "routine-missing" | "routine-ambiguous" -> 0
        | "routine-changed-head" -> 1
        | "routine-protected-operation" -> 2
        | "routine-empty" -> 3
        | _ -> -1

    let private routineFinding (check: Check) (inputs: RoutineInputs) : Finding =
        let withCandidate (decide: RoutineCandidate -> Finding) =
            match inputs.Candidate with
            | Known value -> decide value
            | NotObserved kind -> unknown check PullRequestFact kind []
        let gated (phase: int) (reason: Reason) (evidence: string) =
            match inputs.RoutineGate with
            | NotObserved kind -> unknown check RoutineGate kind []
            | Known Admitted -> supported check evidence
            | Known(GateFinding kind) ->
                let failing = gatePhase kind
                if failing < 0 then unknown check RoutineGate Unreadable []
                elif phase < failing then supported check evidence
                elif phase = failing then unsupported check reason []
                else unknown check RoutineGate NotEvaluated []
        match check with
        | BaseIsDefaultBranch ->
            withCandidate (fun value ->
                if value.BaseRef = inputs.DefaultBranch then supported check "the pull request targets the default branch"
                else unsupported check PullRequestBaseNotDefault [])
        | PullRequestOpen ->
            withCandidate (fun value ->
                match value.State with
                | Some "open" -> supported check "the pull request is open"
                | Some _ -> unsupported check PullRequestNotOpen []
                | None -> unknown check PullRequestFact NotSupplied [])
        | RoutineBranch ->
            withCandidate (fun value ->
                if routineBranch.IsMatch value.HeadRef then supported check "head branch matches the gate's routine branch pattern"
                else unsupported check BranchNotRoutine [])
        | MarkerSingle -> gated 0 MarkerMissingOrAmbiguous "the gate finds exactly one complete marker with no repeated field"
        | MarkerHeadBound -> gated 1 MarkerHeadDiffers "the gate finds the marker bound to the exact head"
        | MarkerOperationAllowed -> gated 2 OperationNotAllowed "the gate finds the marker operation allowed and not protected"
        | ChangesPresent -> gated 3 NoChangedPaths "the exact base and head differ in at least one path"
        | ClosingLinkDeclared ->
            match inputs.ClosingGate with
            | NotObserved kind -> unknown check ClosingGate kind []
            | Known NoClosing -> supported check "the closing-keyword gate finds no closing reference in the title, body or commit messages"
            // A declared closing line is the gate's accepted way to close; whether that fulfils the issue is not decidable here.
            | Known(DeclaredClosing references) -> unknown check ClosingGate (ClosingLinkPresent references.Length) (capped references)
            | Known UndeclaredClosing -> unsupported check ClosingKeywordUndeclared []
        | SinglePullRequest ->
            match inputs.OpenPullRequests with
            | NotObserved kind -> unknown check OpenPullRequestFact kind []
            | Known _ -> unknown check Registry NotEvaluated [] // decided by `runRoutine`, which knows the stage
        | BaseCurrent ->
            withCandidate (fun value ->
                match value.BaseSha, inputs.LiveBaseSha with
                | None, _ -> unknown check PullRequestFact NotSupplied []
                | _, NotObserved kind -> unknown check BaseHead kind []
                | Some recorded, Known live ->
                    if recorded = live then supported check "the recorded base is the live default branch head"
                    else unknown check BaseHead BaseMoved [])
        | _ -> unknown check Registry NotRegistered []

    let runRoutine (stage: Stage) (inputs: RoutineInputs) : Report =
        let checks = checksFor RoutineDelivery stage
        if checks.IsEmpty then unregistered RoutineDelivery stage
        else
            let one (check: Check) =
                try
                    match check, inputs.OpenPullRequests with
                    // The open count means different things at the two stages: none yet before opening, exactly one before merging.
                    | SinglePullRequest, Known count ->
                        if stage = OpenPullRequest then
                            if count = 0 then supported check "no pull request is open for the branch"
                            else unsupported check PullRequestAlreadyOpen [ string count + " open" ]
                        elif count = 1 then supported check "exactly one pull request is open for the branch"
                        else unsupported check PullRequestNotSingle [ string count + " open" ]
                    | _ -> routineFinding check inputs
                with _ -> unknown check Registry NotEvaluated []
            reportOf RoutineDelivery stage (checks |> List.map one)

    // ---- publication writer registration ----

    let private publishEffect =
        Text.RegularExpressions.Regex(
            @"dotnet nuget push|NuGet/login@|gh release (?:create|upload|edit|delete)|softprops/action-gh-release|git push[^\n]*(?:--tags|refs/tags|--atomic)",
            Text.RegularExpressions.RegexOptions.CultureInvariant)

    let private executableLines (text: string) =
        text.Split('\n') |> Array.filter (fun line -> not (Text.RegularExpressions.Regex.IsMatch(line, @"^\s*#"))) |> Array.toList

    let private writersAndExempt (repository: string) (json: string) : Result<Set<string> * Set<string>, unit> =
        try
            use document = JsonDocument.Parse json
            let root = document.RootElement
            let rows (name: string) =
                root.GetProperty(name).EnumerateArray()
                |> Seq.filter (fun row -> row.GetProperty("repo").GetString() = repository)
                |> Seq.collect (fun row -> row.GetProperty("workflows").EnumerateArray() |> Seq.map _.GetString())
                |> Set.ofSeq
            if root.GetProperty("schema").GetInt32() <> 1 then Error()
            else
                let hasExempt, _ = root.TryGetProperty "exempt"
                Ok(rows "writers", (if hasExempt then rows "exempt" else Set.empty))
        with _ -> Error()

    let runPublication (stage: Stage) (inputs: PublicationInputs) : Report =
        let checks = checksFor PublicationWriterRegistration stage
        if checks.IsEmpty then unregistered PublicationWriterRegistration stage
        else
            let registry = match inputs.Registry with Known json -> Some(writersAndExempt inputs.Repository json) | NotObserved _ -> None
            let one (check: Check) =
                try
                    match check with
                    | WorkflowReadable ->
                        (match inputs.Workflow with
                         | Known _ -> supported check "the workflow text was read"
                         | NotObserved kind -> unknown check WorkflowText kind [])
                    | WorkflowPublishes ->
                        (match inputs.Workflow with
                         | NotObserved kind -> unknown check WorkflowText kind []
                         | Known text ->
                            if executableLines text |> List.exists publishEffect.IsMatch then supported check "the workflow carries a publication effect token"
                            else unsupported check WorkflowHasNoPublicationEffect [])
                    | RegistryReadable ->
                        (match inputs.Registry, registry with
                         | NotObserved kind, _ -> unknown check WriterRegistryText kind []
                         | _, Some(Ok _) -> supported check "the writer registry parses (schema 1)"
                         | _ -> unknown check WriterRegistryText Unreadable [])
                    | NotYetRegistered ->
                        (match inputs.Registry, registry with
                         | NotObserved kind, _ -> unknown check WriterRegistryText kind []
                         | _, Some(Ok(writers, exempt)) ->
                            if writers.Contains inputs.WorkflowPath || exempt.Contains inputs.WorkflowPath then unsupported check WriterAlreadyRegistered []
                            else supported check "the workflow is neither a registered writer nor exempt"
                         | _ -> unknown check WriterRegistryText Unreadable [])
                    | WriterRegistered ->
                        (match inputs.Registry, registry with
                         | NotObserved kind, _ -> unknown check WriterRegistryText kind []
                         | _, Some(Ok(writers, _)) ->
                            if writers.Contains inputs.WorkflowPath then supported check "the workflow is a registered writer"
                            else unsupported check WriterNotRegistered []
                         | _ -> unknown check WriterRegistryText Unreadable [])
                    | CoverageAudit ->
                        (match inputs.CoverageAudit with
                         | NotObserved kind -> unknown check CoverageGate kind []
                         | Known Clean -> supported check "the maintained coverage gate reports zero unmet"
                         | Known(Unmet count) -> { unsupported check CoverageUnmet [] with Subjects = [ string count + " unmet" ] })
                    | _ -> unknown check Registry NotRegistered []
                with _ -> unknown check Registry NotEvaluated []
            reportOf PublicationWriterRegistration stage (checks |> List.map one)

    // ---- observation helpers ----

    let headroomOf (levels: (string * string) list) : Observed<int64 option> =
        let parse (text: string) =
            let mutable value = 0L
            if Int64.TryParse(text.Trim(), Globalization.NumberStyles.None, Globalization.CultureInfo.InvariantCulture, &value) then Some value else None
        if levels.IsEmpty then NotObserved Unreadable
        else
            let decided =
                levels |> List.map (fun (limit, current) ->
                    if isNull limit || isNull current then Error()
                    elif limit.Trim() = "max" then Ok None
                    else
                        match parse limit, parse current with
                        | Some maximum, Some used -> Ok(Some(maximum - used))
                        | _ -> Error())
            if decided |> List.exists Result.isError then NotObserved Unreadable
            else
                match decided |> List.choose (function Ok(Some value) -> Some value | _ -> None) with
                | [] -> Known None
                | values -> Known(Some(List.min values))

    let lockedContentHash (packageName: string) (lockJson: string) : Observed<string> =
        try
            use document = JsonDocument.Parse lockJson
            let hashes =
                document.RootElement.GetProperty("dependencies").EnumerateObject()
                |> Seq.collect (fun framework -> framework.Value.EnumerateObject())
                |> Seq.filter (fun package -> String.Equals(package.Name, packageName, StringComparison.OrdinalIgnoreCase))
                |> Seq.map (fun package -> package.Value.GetProperty("contentHash").GetString())
                |> Seq.distinct
                |> Seq.toList
            match hashes with
            | [ hash ] when not (String.IsNullOrWhiteSpace hash) -> Known hash
            | _ -> NotObserved Unreadable
        with _ -> NotObserved Unreadable

    let metadataContentHash (metadataJson: string) : Observed<string> =
        try
            use document = JsonDocument.Parse metadataJson
            let hash = document.RootElement.GetProperty("contentHash").GetString()
            if String.IsNullOrWhiteSpace hash then NotObserved Unreadable else Known hash
        with _ -> NotObserved Unreadable

    // ---- rendering ----

    let toJson (report: Report) : string =
        let root = JsonObject()
        root.["schema"] <- JsonValue.Create "fsgg.board-v2-preflight/1"
        root.["route"] <- JsonValue.Create(routeCode report.Route)
        root.["stage"] <- JsonValue.Create(stageCode report.Stage)
        root.["overall"] <- JsonValue.Create(match report.Overall with AllSupported -> "SupportedForNextStage" | AnyUnsupported -> "Unsupported" | AnyUnknown -> "Unknown")
        root.["exitCode"] <- JsonValue.Create(exitCode report.Overall)
        let findings = JsonArray()
        for finding in report.Findings do
            let row = JsonObject()
            row.["check"] <- JsonValue.Create(checkCode finding.Check)
            match finding.Verdict with
            | SupportedForNextStage evidence ->
                row.["verdict"] <- JsonValue.Create "SupportedForNextStage"
                row.["evidence"] <- JsonValue.Create evidence
            | Unsupported(reason, supplier) ->
                row.["verdict"] <- JsonValue.Create "Unsupported"
                row.["reason"] <- JsonValue.Create(reasonCode reason)
                row.["supplier"] <- JsonValue.Create(supplierCode supplier)
            | Unknown gap ->
                row.["verdict"] <- JsonValue.Create "Unknown"
                row.["gap"] <- JsonValue.Create(gapCode gap)
                match gap.Kind with
                | BareMembershipsObserved _ ->
                    row.["note"] <- JsonValue.Create "Unenrolled memberships lacking Status, Track or Work level; that they break the installed caller is unproven (the caller worked with such memberships present on 2026-10-30; a sudden rise in bare unenrolled memberships matched the observed refusal)."
                | _ -> ()
            row.["subjects"] <- JsonArray(finding.Subjects |> List.map (fun name -> JsonValue.Create name :> JsonNode) |> List.toArray)
            findings.Add row
        root.["findings"] <- findings
        root.ToJsonString()
