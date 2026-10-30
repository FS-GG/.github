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

    let routes = [ ProgrammeEnrollmentSuccessor; RoutineDelivery; PublicationWriterRegistration ]
    let stages = [ PlanSuccessor; PrepareSuccessor; BindRuntime ]

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

    // The next stage is the one about to run. Churn matters only where the installed caller is next;
    // auto-added unseeded items break that caller, so they are checked from the first stage.
    let private planChecks =
        [ ConstructorIdentity; PredecessorIntegrity; RuntimeBindingIntegrity; FactsComplete; IssueKind; IssueIdentity
          ProjectMembership; RepositoryInBinding; NotAlreadyEnrolled; RoadmapConflict; PlanGrammar; PlanBlob; RowCap
          PayloadBound; TemplateBound; ToolchainLock; MemoryHeadroom; ProjectAutoAdd ]

    let private bindChecks =
        [ ConstructorIdentity; PredecessorIntegrity; RuntimeBindingIntegrity; PlanGrammar; PlanBlob; ToolchainLock
          MemoryHeadroom; ProjectAutoAdd; AdjacencyChurn ]

    let checksFor route stage =
        match route, stage with
        | ProgrammeEnrollmentSuccessor, (PlanSuccessor | PrepareSuccessor) -> planChecks
        | ProgrammeEnrollmentSuccessor, BindRuntime -> bindChecks
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
        | UnenrolledItemLacksSchedulingFields

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
        | UnenrolledItemLacksSchedulingFields -> "UnenrolledItemLacksSchedulingFields"

    let private supplierOf reason =
        match reason with
        | ConstructorDiffersFromMain -> ConstructorBuild
        | PredecessorCommitmentMismatch | PredecessorNotSpliceable | RuntimeBindingDiffers -> PredecessorCustody
        | DuplicateRequestIssue | IssueIsPullRequest | NodeIdentityMismatch | RepositoryOutsideBinding | AlreadyEnrolled -> RequestAuthor
        | NotProjectMember | ConflictingHumanRoadmap | UnenrolledItemLacksSchedulingFields -> Root
        | PlanNotAdmitted | PlanBlobAbsentOnMain -> PlanAuthor
        | RowCapExceeded | PayloadOverBound | BindingOverBound -> ProtocolRelease
        | LockedHashDiffersFromCache -> ToolchainCache
        | InsufficientMemoryHeadroom -> HostCapacity

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
        | Registry
        | Planner

    type GapKind =
        | NotSupplied
        | Unreadable
        | Incomplete
        | NotLive
        | NotInKnownTable
        | NotEvaluated
        | ChurnObserved of count: int
        | NotRegistered

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
        | NotRegistered -> "NotRegistered"

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

    type EnrollmentInputs =
        { Request: V2Enrollment.Request
          Facts: Observed<V2Enrollment.NativeFacts * FactsFreshness>
          ConstructorRevision: Observed<string>
          ProtectedMain: Observed<string>
          PredecessorPrivate: Observed<string>
          PredecessorPublic: Observed<string>
          RuntimeBinding: Observed<string>
          InstalledReaderSha256: Observed<string>
          KnownInstalledReaders: Set<string>
          LockedFSharpCoreHash: Observed<string>
          CachedFSharpCoreHash: Observed<string>
          MemoryHeadroomBytes: Observed<int64 option>
          MinimumHeadroomBytes: int64
          PayloadLimitBytes: int
          BindingLimitBytes: int
          ProjectItems: Observed<ProjectItem list>
          Now: DateTimeOffset
          ChurnWindow: TimeSpan }

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
          Binding: V2Projection.Binding option
          Analysis: Result<V2Enrollment.Analysis, V2Enrollment.Refusal> option
          FactsKnown: V2Enrollment.NativeFacts option }

    let private emptyFacts : V2Enrollment.NativeFacts = { ProjectId = ""; Issues = []; PlanBlobs = [] }

    let private context (seams: V2Enrollment.Seams) (inputs: EnrollmentInputs) : Context =
        let binding =
            match inputs.PredecessorPrivate with
            | Known json -> (match V2Projection.decodeBinding json with Ok value -> Some value | Error _ -> None)
            | NotObserved _ -> None
        let facts = match inputs.Facts with Known(facts, _) -> Some facts | NotObserved _ -> None
        let analysis =
            match inputs.PredecessorPrivate, inputs.PredecessorPublic with
            | Known privateJson, Known publicJson ->
                // Without facts only the facts-independent findings are consulted; the rest report their own gap.
                Some(V2Enrollment.analyzeWith seams privateJson publicJson inputs.Request (defaultArg facts emptyFacts))
            | _ -> None
        { Inputs = inputs; Seams = seams; Binding = binding; Analysis = analysis; FactsKnown = facts }

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
            if built = main then supported ConstructorIdentity "constructor source revision equals the intended protected main"
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

    let private planGrammar (ctx: Context) =
        let protectedDocs = ctx.Inputs.Request.Issues |> List.filter (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc _ -> true | _ -> false)
        let source = fromAnalysis ctx PlanGrammar false V2Enrollment.PlanNotAdmitted PlanNotAdmitted "every plan reference is admitted by the source plan grammar"
        match source.Verdict with
        | SupportedForNextStage _ when not protectedDocs.IsEmpty ->
            // The source grammar admits the references; whether the installed reader's grammar is the same
            // is known only for artifacts in the table.
            match ctx.Inputs.InstalledReaderSha256 with
            | NotObserved kind -> unknown PlanGrammar InstalledReader kind []
            | Known sha ->
                if ctx.Inputs.KnownInstalledReaders.IsEmpty then unknown PlanGrammar KnownReaderTable NotSupplied []
                elif ctx.Inputs.KnownInstalledReaders.Contains sha then supported PlanGrammar "source grammar admits every reference and the installed reader is in the known table"
                else unknown PlanGrammar InstalledReader NotInKnownTable []
        | _ -> source

    let private planBlob (ctx: Context) =
        let docs = ctx.Inputs.Request.Issues |> List.filter (fun issue -> match issue.Plan with V2Enrollment.ProtectedDoc _ -> true | _ -> false)
        match predecessorGap ctx, ctx.Inputs.Facts with
        | _, NotObserved kind -> unknown PlanBlob NativeFacts kind []
        | _ when docs.IsEmpty -> supported PlanBlob "no protected document reference"
        | _, Known(_, Supplied) -> unknown PlanBlob NativeFacts NotLive []
        | _, Known(_, Live) ->
            fromAnalysis ctx PlanBlob true V2Enrollment.PlanBlobAbsentOnMain PlanBlobAbsentOnMain "every protected document exists on main now"

    let private rowCap (ctx: Context) =
        match predecessorGap ctx, ctx.Analysis with
        | Some(input, kind), _ -> unknown RowCap input kind []
        | None, Some(Ok analysis) ->
            let total = analysis.PredecessorRows + ctx.Inputs.Request.Issues.Length
            if total > V2Enrollment.rowCap then
                { unsupported RowCap RowCapExceeded [] with Subjects = [ string analysis.PredecessorRows + "+" + string ctx.Inputs.Request.Issues.Length + ">" + string V2Enrollment.rowCap ] }
            else supported RowCap (string analysis.PredecessorRows + "+" + string ctx.Inputs.Request.Issues.Length + "<=" + string V2Enrollment.rowCap)
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
            match ctx.Inputs.PredecessorPrivate, ctx.Binding with
            | Known privateJson, Some binding ->
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
                ignore binding
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
        match ctx.Inputs.MemoryHeadroomBytes with
        | NotObserved kind -> unknown MemoryHeadroom CgroupMemory kind []
        | Known None -> supported MemoryHeadroom "no cgroup memory limit applies"
        | Known(Some headroom) ->
            if headroom < ctx.Inputs.MinimumHeadroomBytes then
                { unsupported MemoryHeadroom InsufficientMemoryHeadroom [] with Subjects = [ string headroom + "<" + string ctx.Inputs.MinimumHeadroomBytes ] }
            else supported MemoryHeadroom (string headroom + ">=" + string ctx.Inputs.MinimumHeadroomBytes + " bytes headroom")

    let private nameOf (item: ProjectItem) =
        match item.Content with
        | IssueContent(reference, _) | PullRequestContent(reference, _) -> reference
        | OtherContent kind -> kind + ":" + item.ItemId

    let private projectAutoAdd (ctx: Context) =
        match ctx.Inputs.ProjectItems, ctx.Inputs.PredecessorPrivate, ctx.Binding with
        | NotObserved kind, _, _ -> unknown ProjectAutoAdd ProjectItems kind []
        | _, NotObserved kind, _ -> unknown ProjectAutoAdd PredecessorPrivate kind []
        | _, _, None -> unknown ProjectAutoAdd PredecessorPrivate Unreadable []
        | Known items, _, Some binding ->
            let enrolled (item: ProjectItem) =
                match item.Content with
                | IssueContent(_, node) -> binding.SelectedIssues.ContainsKey node
                | _ -> false
            let unenrolled = items |> List.filter (enrolled >> not)
            let offenders = unenrolled |> List.filter (fun item -> not (item.HasStatus && item.HasTrack && item.HasWorkLevel))
            if offenders.IsEmpty then supported ProjectAutoAdd (string unenrolled.Length + " unenrolled item(s), each with Status, Track and Work level")
            else
                // Each offender is named with exactly which of the three fields it lacks, so a fully bare
                // membership can be told from a legacy one that merely lacks Track and Work level.
                let named (item: ProjectItem) =
                    let missing = [ (if item.HasStatus then None else Some "Status"); (if item.HasTrack then None else Some "Track"); (if item.HasWorkLevel then None else Some "Work level") ] |> List.choose id
                    nameOf item + "[" + String.Join(",", missing) + "]"
                unsupported ProjectAutoAdd UnenrolledItemLacksSchedulingFields (offenders |> List.map named |> capped)

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

    let runWith (seams: V2Enrollment.Seams) (route: Route) (stage: Stage) (inputs: EnrollmentInputs) : Report =
        let checks = checksFor route stage
        let findings =
            if checks.IsEmpty then [ unknown RouteRegistry Registry NotRegistered [] ]
            else
                let ctx = context seams inputs
                checks |> List.map (fun check -> try evaluate ctx check with _ -> unknown check Registry NotEvaluated [])
        { Route = route; Stage = stage; Findings = findings; Overall = overallOf findings }

    let run route stage inputs = runWith V2Enrollment.defaultSeams route stage inputs

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
            row.["subjects"] <- JsonArray(finding.Subjects |> List.map (fun name -> JsonValue.Create name :> JsonNode) |> List.toArray)
            findings.Add row
        root.["findings"] <- findings
        root.ToJsonString()
