namespace FS.GG.Coord.Tests

/// Supported-route preflight controls. Pure and offline; synthetic identities only, every private value a sentinel.
module V2PreflightTests =
    open System
    open System.Text
    open System.Text.Json.Nodes
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Enrollment
    open FS.GG.Coord.GitHub.V2Preflight
    open FS.GG.Coord.GitHub.V2NativeResolve
    open FS.GG.Coord.Tests.V2EnrollmentTests

    let private main = String.replicate 40 "1"
    let private reader = String.replicate 64 "a"
    let private hash = "synthetic-content-hash=="
    let private now = DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)

    let private item (reference: string) (node: string) (status: bool) (track: bool) (level: bool) (updated: DateTimeOffset) : ProjectItem =
        { ItemId = "PVTI_" + node; Content = IssueContent(reference, node); HasStatus = status; HasTrack = track; HasWorkLevel = level
          RoadmapRaw = None; UpdatedAt = updated }

    let private enrolledItems (count: int) =
        [ for i in 0 .. count - 1 -> item $"FS-GG/.github#{70001 + i}" $"I_synthetic_{sentinel}_{i}" true true true (now - TimeSpan.FromDays 2.0) ]

    let private runtimeOf (privateJson: string) (population: string) =
        let node = JsonNode.Parse privateJson
        node.["populationRevision"] <- JsonValue.Create population
        node.ToJsonString()

    let private successorMerge = String.replicate 40 "5"

    /// The successor pair a prepared cohort would deliver, and the public blob at its merge.
    let private successorPair (privateJson: string) (publicJson: string) =
        let built = plan privateJson publicJson request facts |> get
        V2Projection.prepareProgrammeBinding built.TemplateJson built.Payload (fun () -> Array.create 32 9uy) |> getIo

    /// Healthy inputs over a real predecessor pair. Every control below changes one thing.
    let private healthy (count: int) : EnrollmentInputs =
        let privateJson, publicJson, _ = predecessor count ""
        let successorPrivate, successorPublic = if count <= 247 then successorPair privateJson publicJson else privateJson, publicJson
        { Request = request
          Facts = Known(facts, Live)
          ConstructorRevision = Known main
          ProtectedMain = Known main
          ProtectedMainFreshness = LiveMain
          PredecessorPrivate = Known privateJson
          PredecessorPublic = Known publicJson
          RuntimeBinding = Known(runtimeOf privateJson mergeSha)
          SuccessorPrivate = Known successorPrivate
          SuccessorMergeSha = Known successorMerge
          SuccessorPublic = Known successorPublic
          InstalledReaderSha256 = Known reader
          KnownInstalledReaders = Set.ofList [ reader ]
          KnownLiftedCapReaders = Set.ofList [ reader ]
          LockedFSharpCoreHash = Known hash
          CachedFSharpCoreHash = Known hash
          MemoryHeadroomBytes = Known(Some 4294967296L)
          HostMemAvailableBytes = Known 8589934592L
          MinimumHeadroomBytes = 1073741824L
          PayloadLimitBytes = payloadLimitBytes
          BindingLimitBytes = bindingLimitBytes
          ProjectItems = Known(enrolledItems count)
          InvisibleRepositories = Set.empty
          Now = now
          ChurnWindow = TimeSpan.FromMinutes 10.0 }

    let private verdictOf (report: Report) (check: Check) =
        (report.Findings |> List.find (fun finding -> finding.Check = check)).Verdict

    let private finding (report: Report) (check: Check) = report.Findings |> List.find (fun finding -> finding.Check = check)

    let private isSupported = function SupportedForNextStage _ -> true | _ -> false

    let private runPlan inputs = run ProgrammeEnrollmentSuccessor PlanSuccessor inputs

    let private expectUnsupported (check: Check) (reason: Reason) (supplier: Supplier) (report: Report) =
        Assert.Equal(Unsupported(reason, supplier), verdictOf report check)
        Assert.Equal(AnyUnsupported, report.Overall)
        Assert.Equal(10, exitCode report.Overall)

    let private expectUnknown (check: Check) (input: Input) (kind: GapKind) (report: Report) =
        Assert.Equal(Unknown { Input = input; Kind = kind }, verdictOf report check)
        Assert.NotEqual(0, exitCode report.Overall)

    let private withFact0 (change: NativeIssueFact -> NativeIssueFact) (inputs: EnrollmentInputs) =
        match inputs.Facts with
        | Known(facts, freshness) -> { inputs with Facts = Known({ facts with Issues = change facts.Issues.[0] :: facts.Issues.Tail }, freshness) }
        | _ -> inputs

    // ---- closed registry, verdict algebra, exit codes ----

    [<Fact>]
    let ``healthy inputs support every registered check of every enrolled stage and exit 0`` () =
        for stage in [ PlanSuccessor; PrepareSuccessor; BindRuntime ] do
            let report = run ProgrammeEnrollmentSuccessor stage (healthy 5)
            Assert.Equal<Check list>(checksFor ProgrammeEnrollmentSuccessor stage, report.Findings |> List.map _.Check)
            for finding in report.Findings do
                match finding.Verdict with
                | SupportedForNextStage evidence -> Assert.False(String.IsNullOrWhiteSpace evidence)
                | other -> failwithf "%s %s: %A" (stageCode stage) (checkCode finding.Check) other
            Assert.Equal((AllSupported, 0), (report.Overall, exitCode report.Overall))
        Assert.Equal(18, (checksFor ProgrammeEnrollmentSuccessor PlanSuccessor).Length)
        Assert.Equal(9, (checksFor ProgrammeEnrollmentSuccessor BindRuntime).Length)

    [<Fact>]
    let ``the three overall verdicts have distinct exit codes, unknown is never zero and unsupported dominates`` () =
        let codes = [ AllSupported; AnyUnsupported; AnyUnknown ] |> List.map exitCode
        Assert.Equal<int list>([ 0; 10; 11 ], codes)
        Assert.Equal(3, (Set.ofList codes).Count)
        let supported = { Check = RowCap; Verdict = SupportedForNextStage "x"; Subjects = [] }
        let unknown = { Check = RowCap; Verdict = Unknown { Input = Registry; Kind = NotRegistered }; Subjects = [] }
        let unsupported = { Check = RowCap; Verdict = Unsupported(RowCapExceeded, ProtocolRelease); Subjects = [] }
        Assert.Equal(AllSupported, overallOf [ supported; supported ])
        Assert.Equal(AnyUnknown, overallOf [ supported; unknown ])
        Assert.Equal(AnyUnsupported, overallOf [ supported; unknown; unsupported ])
        Assert.Equal(AnyUnknown, overallOf [])
        Assert.NotEqual(0, exitCode (overallOf []))

    [<Fact>]
    let ``a route and stage that do not belong together are an explicit unknown, never supported`` () =
        Assert.Equal(3, routes.Length)
        let enrollment = [ PlanSuccessor; PrepareSuccessor; BindRuntime ]
        let routine = [ OpenPullRequest; MergePullRequest ]
        let publication = [ RegisterWriter; RunWriter ]
        let belongs route stage =
            match route with
            | ProgrammeEnrollmentSuccessor -> List.contains stage enrollment
            | RoutineDelivery -> List.contains stage routine
            | PublicationWriterRegistration -> List.contains stage publication
        let mutable pairs = 0
        for route in routes do
            for stage in stages do
                pairs <- pairs + 1
                // Registered exactly where they belong together.
                Assert.Equal(belongs route stage, not (checksFor route stage).IsEmpty)
                if not (belongs route stage) then
                    let report = run route stage (healthy 5)
                    Assert.Equal<Check list>([ RouteRegistry ], report.Findings |> List.map _.Check)
                    Assert.Equal(Unknown { Input = Registry; Kind = NotRegistered }, verdictOf report RouteRegistry)
                    Assert.Equal((AnyUnknown, 11), (report.Overall, exitCode report.Overall))
        Assert.Equal(21, pairs)

    [<Fact>]
    let ``codes are the closed printable case names`` () =
        Assert.Equal(3, (routes |> List.map routeCode |> Set.ofList).Count)
        Assert.Equal(7, (stages |> List.map stageCode |> Set.ofList).Count)
        Assert.Equal("ProjectAutoAdd", checkCode ProjectAutoAdd)
        Assert.Equal("Root", supplierCode Root)
        Assert.Equal("InstalledReader/NotInKnownTable", gapCode { Input = InstalledReader; Kind = NotInKnownTable })
        Assert.Equal("ProjectItems/ChurnObserved:3", gapCode { Input = ProjectItems; Kind = ChurnObserved 3 })
        Assert.Equal("ProjectItems/BareMembershipsObserved:2", gapCode { Input = ProjectItems; Kind = BareMembershipsObserved 2 })

    // ---- constructor identity ----

    [<Fact>]
    let ``constructor identity: a different source revision is unsupported and an unreadable one is unknown`` () =
        let healthyReport = runPlan (healthy 5)
        Assert.True(isSupported (verdictOf healthyReport ConstructorIdentity))
        expectUnsupported ConstructorIdentity ConstructorDiffersFromMain ConstructorBuild (runPlan { healthy 5 with ConstructorRevision = Known(String.replicate 40 "2") })
        expectUnknown ConstructorIdentity SourceRevision Unreadable (runPlan { healthy 5 with ConstructorRevision = NotObserved Unreadable })
        expectUnknown ConstructorIdentity ProtectedMain NotSupplied (runPlan { healthy 5 with ProtectedMain = NotObserved NotSupplied })

    // ---- predecessor integrity and runtime binding ----

    [<Fact>]
    let ``predecessor integrity: a public blob that is not the commitment, an unspliceable payload and unreadable custody`` () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let altered = publicJson.Replace(publicJson.Substring(publicJson.Length - 10, 4), "0000")
        let report = runPlan { healthy 5 with PredecessorPublic = Known altered }
        expectUnsupported PredecessorIntegrity PredecessorCommitmentMismatch PredecessorCustody report
        // The dependent row checks are not decidable, so they are unknown rather than silently supported.
        expectUnknown IssueKind PredecessorPrivate NotEvaluated report
        let newlinePrivate, newlinePublic, _ = predecessor 5 "\n"
        let unspliceable = runPlan { healthy 5 with PredecessorPrivate = Known newlinePrivate; PredecessorPublic = Known newlinePublic; RuntimeBinding = Known(runtimeOf newlinePrivate mergeSha) }
        expectUnsupported PredecessorIntegrity PredecessorNotSpliceable PredecessorCustody unspliceable
        expectUnknown PredecessorIntegrity PredecessorPrivate Unreadable (runPlan { healthy 5 with PredecessorPrivate = NotObserved Unreadable })
        expectUnknown PredecessorIntegrity PredecessorPublic Unreadable (runPlan { healthy 5 with PredecessorPublic = NotObserved Unreadable })
        ignore privateJson

    [<Fact>]
    let ``runtime binding must differ from the private pair only in populationRevision, which is the recorded merge`` () =
        let privateJson, _, _ = predecessor 5 ""
        Assert.True(isSupported (verdictOf (runPlan (healthy 5)) RuntimeBindingIntegrity))
        // One other field changes.
        let drifted = JsonNode.Parse(runtimeOf privateJson mergeSha)
        drifted.["projectNumber"] <- JsonValue.Create 4
        expectUnsupported RuntimeBindingIntegrity RuntimeBindingDiffers PredecessorCustody (runPlan { healthy 5 with RuntimeBinding = Known(drifted.ToJsonString()) })
        // Only populationRevision differs, but it is not the recorded merge.
        expectUnsupported RuntimeBindingIntegrity RuntimeBindingDiffers PredecessorCustody (runPlan { healthy 5 with RuntimeBinding = Known(runtimeOf privateJson (String.replicate 40 "3")) })
        expectUnknown RuntimeBindingIntegrity RuntimeBinding NotSupplied (runPlan { healthy 5 with RuntimeBinding = NotObserved NotSupplied })
        expectUnsupported RuntimeBindingIntegrity RuntimeBindingDiffers PredecessorCustody (runPlan { healthy 5 with RuntimeBinding = Known "{" })

    // ---- per-row checks, one negative control each ----

    [<Fact>]
    let ``each per-row check is unsupported with its exact reason, supplier and named issue`` () =
        let ref0, ref1 = request.Issues.[0].Ref, request.Issues.[1].Ref
        let named (check: Check) (names: string list) (report: Report) = Assert.Equal<string list>(names, (finding report check).Subjects)
        let issue1 change (inputs: EnrollmentInputs) =
            match inputs.Facts with
            | Known(facts, freshness) -> { inputs with Facts = Known({ facts with Issues = facts.Issues.Head :: [ change facts.Issues.[1] ] }, freshness) }
            | _ -> inputs
        let pullRequest = runPlan (withFact0 (fun f -> { f with Kind = NativePullRequest }) (healthy 5))
        expectUnsupported IssueKind IssueIsPullRequest RequestAuthor pullRequest
        named IssueKind [ ref0 ] pullRequest
        let node = runPlan (withFact0 (fun f -> { f with NodeId = "X_not_an_issue" }) (healthy 5))
        expectUnsupported IssueIdentity NodeIdentityMismatch RequestAuthor node
        named IssueIdentity [ ref0 ] node
        let notMember = runPlan (issue1 (fun f -> { f with ProjectMember = false }) (healthy 5))
        expectUnsupported ProjectMembership NotProjectMember Root notMember
        named ProjectMembership [ ref1 ] notMember
        let outside = healthy 5
        let outsideRequest = { outside.Request with Issues = { outside.Request.Issues.[0] with Ref = "FS-GG/Other#9" } :: outside.Request.Issues.Tail }
        let outsideReport = runPlan { outside with Request = outsideRequest }
        expectUnsupported RepositoryInBinding RepositoryOutsideBinding RequestAuthor outsideReport
        named RepositoryInBinding [ "FS-GG/Other#9" ] outsideReport
        let enrolled = withFact0 (fun f -> { f with Ref = "FS-GG/.github#70001"; NodeId = "I_synthetic_" + sentinel + "_0" }) { healthy 5 with Request = { request with Issues = { request.Issues.[0] with Ref = "FS-GG/.github#70001" } :: request.Issues.Tail } }
        expectUnsupported NotAlreadyEnrolled AlreadyEnrolled RequestAuthor (runPlan enrolled)
        let human = runPlan (withFact0 (fun f -> { f with HumanRoadmap = Some "https://github.com/FS-GG/.github/issues/4930" }) (healthy 5))
        expectUnsupported RoadmapConflict ConflictingHumanRoadmap Root human
        // Duplicate request issue is decided without facts.
        let duplicated = { healthy 5 with Request = { request with Issues = request.Issues @ [ request.Issues.[0] ] } }
        expectUnsupported IssueIdentity DuplicateRequestIssue RequestAuthor (runPlan duplicated)
        expectUnsupported IssueIdentity DuplicateRequestIssue RequestAuthor (runPlan { duplicated with Facts = NotObserved NotSupplied })

    [<Fact>]
    let ``partial visibility and unobserved facts are unknown, and facts-independent checks still decide`` () =
        let partial = runPlan (withFact0 (fun f -> { f with Complete = false }) (healthy 5))
        Assert.Equal(Unknown { Input = NativeFacts; Kind = Incomplete }, verdictOf partial FactsComplete)
        Assert.Equal<string list>([ request.Issues.[0].Ref ], (finding partial FactsComplete).Subjects)
        Assert.Equal((AnyUnknown, 11), (partial.Overall, exitCode partial.Overall))
        let missing = runPlan { healthy 5 with Facts = NotObserved Incomplete }
        for check in [ FactsComplete; IssueKind; ProjectMembership; NotAlreadyEnrolled; RoadmapConflict; PlanBlob ] do
            Assert.Equal(Unknown { Input = NativeFacts; Kind = Incomplete }, verdictOf missing check)
        Assert.True(isSupported (verdictOf missing RowCap))
        Assert.True(isSupported (verdictOf missing RepositoryInBinding))
        Assert.Equal(AnyUnknown, missing.Overall)

    // ---- plan reference: grammar, installed table, blob on main now ----

    let private outcomeUrlInputs () =
        let inputs = healthy 5
        let docRequest = { inputs.Request with Issues = [ { inputs.Request.Issues.[1] with Plan = ProtectedDoc("FS-GG/.github", "issues/4930") } ] }
        let docFacts = { facts with Issues = [ facts.Issues.[1] ]; PlanBlobs = [ { Repository = "FS-GG/.github"; Path = "issues/4930"; OnMain = true } ] }
        { inputs with Request = docRequest; Facts = Known(docFacts, Live) }

    [<Fact>]
    let ``causal control: the installed grammar alone makes an Outcome issue URL plan reference unsupported`` () =
        let inputs = outcomeUrlInputs ()
        let report = runPlan inputs
        expectUnsupported PlanGrammar PlanNotAdmitted PlanAuthor report
        Assert.Equal<string list>([ inputs.Request.Issues.[0].Ref ], (finding report PlanGrammar).Subjects)
        // Replace only the grammar stage with an accept-all stage: the same inputs are now supported, so the grammar caused the verdict.
        let permissive = { V2Enrollment.defaultSeams with Grammar = fun _ url -> Ok("FS-GG/.github", "main", url.Substring(url.IndexOf "/blob/main/" + "/blob/main/".Length)) }
        let relaxed = runWith permissive ProgrammeEnrollmentSuccessor PlanSuccessor inputs
        Assert.True(isSupported (verdictOf relaxed PlanGrammar))
        Assert.Equal(AllSupported, relaxed.Overall)

    [<Fact>]
    let ``the installed reader grammar is unknown unless the installed artifact sha is in the known table`` () =
        let report = runPlan (healthy 5)
        Assert.True(isSupported (verdictOf report PlanGrammar))
        expectUnknown PlanGrammar InstalledReader NotInKnownTable (runPlan { healthy 5 with InstalledReaderSha256 = Known(String.replicate 64 "b") })
        expectUnknown PlanGrammar KnownReaderTable NotSupplied (runPlan { healthy 5 with KnownInstalledReaders = Set.empty })
        expectUnknown PlanGrammar InstalledReader NotSupplied (runPlan { healthy 5 with InstalledReaderSha256 = NotObserved NotSupplied })
        // Causal: only the table membership changes between unknown and supported.
        let other = String.replicate 64 "b"
        Assert.True(isSupported (verdictOf (runPlan { healthy 5 with InstalledReaderSha256 = Known other; KnownInstalledReaders = Set.ofList [ other ] }) PlanGrammar))
        // A request with no protected document has nothing the installed grammar must read.
        let only3008 = { healthy 5 with Request = { request with Issues = [ request.Issues.[0] ] }; InstalledReaderSha256 = NotObserved NotSupplied }
        Assert.True(isSupported (verdictOf (runPlan only3008) PlanGrammar))

    [<Fact>]
    let ``plan blob on main now: absent is unsupported, supplied facts are not live, and live presence is supported`` () =
        let inputs = healthy 5
        Assert.True(isSupported (verdictOf (runPlan inputs) PlanBlob))
        let absent = { inputs with Facts = Known({ facts with PlanBlobs = [] }, Live) }
        let report = runPlan absent
        expectUnsupported PlanBlob PlanBlobAbsentOnMain PlanAuthor report
        Assert.Equal<string list>([ request.Issues.[1].Ref ], (finding report PlanBlob).Subjects)
        expectUnknown PlanBlob NativeFacts NotLive (runPlan { inputs with Facts = Known(facts, Supplied) })

    // ---- row cap and size bounds ----

    [<Fact>]
    let ``row cap: the legacy ceiling needs no reader, the lifted range needs a known reader, the new ceiling is a refusal`` () =
        // At the legacy ceiling every reader reads the successor.
        let legacy = runPlan { healthy 98 with InstalledReaderSha256 = NotObserved NotSupplied; KnownLiftedCapReaders = Set.empty }
        Assert.True(isSupported (verdictOf legacy RowCap))
        Assert.StartsWith("98+2<=100", (match verdictOf legacy RowCap with SupportedForNextStage e -> e | _ -> ""))
        // 99+2 = 101 is above it: supported only by a reader named in the lifted-cap table.
        let beyond = healthy 99
        Assert.StartsWith("99+2<=250", (match verdictOf (runPlan beyond) RowCap with SupportedForNextStage e -> e | other -> failwithf "%A" other))
        expectUnknown RowCap KnownReaderTable NotSupplied (runPlan { beyond with KnownLiftedCapReaders = Set.empty })
        expectUnknown RowCap InstalledReader NotInKnownTable (runPlan { beyond with KnownLiftedCapReaders = Set.ofList [ String.replicate 64 "c" ] })
        expectUnknown RowCap InstalledReader NotSupplied (runPlan { beyond with InstalledReaderSha256 = NotObserved NotSupplied })
        // The new ceiling: 248+2 = 250 passes, 249+2 = 251 is unsupported with the counts named.
        Assert.StartsWith("248+2<=250", (match verdictOf (runPlan (healthy 248)) RowCap with SupportedForNextStage e -> e | other -> failwithf "%A" other))
        let over = runPlan (healthy 249)
        expectUnsupported RowCap RowCapExceeded ProtocolRelease over
        Assert.Equal<string list>([ "249+2>250" ], (finding over RowCap).Subjects)
        expectUnknown RowCap PredecessorPrivate Unreadable (runPlan { healthy 98 with PredecessorPrivate = NotObserved Unreadable })

    [<Fact>]
    let ``payload and binding bounds are computed from the inputs and a lowered limit flips them`` () =
        let inputs = healthy 5
        let ok = runPlan inputs
        Assert.True(isSupported (verdictOf ok PayloadBound))
        Assert.True(isSupported (verdictOf ok TemplateBound))
        let tight = runPlan { inputs with PayloadLimitBytes = 100; BindingLimitBytes = 100 }
        expectUnsupported PayloadBound PayloadOverBound ProtocolRelease tight
        Assert.Equal(Unsupported(BindingOverBound, ProtocolRelease), verdictOf tight TemplateBound)
        // The estimate is exact for a built successor: the limit equal to the real size passes and one below it fails.
        let privateJson, publicJson, _ = predecessor 5 ""
        let built = plan privateJson publicJson request facts |> get
        Assert.True(isSupported (verdictOf (runPlan { inputs with PayloadLimitBytes = built.Payload.Length }) PayloadBound))
        expectUnsupported PayloadBound PayloadOverBound ProtocolRelease (runPlan { inputs with PayloadLimitBytes = built.Payload.Length - 1 })
        // A predecessor that is nearly the installed payload bound cannot take two more rows; it is not buildable, and is still decided.
        let fatPrivate, fatPublic, fatPayload = predecessorPadded 90 "" 1280
        Assert.True(fatPayload.Length < payloadLimitBytes && fatPayload.Length > payloadLimitBytes - 500, string fatPayload.Length)
        let fat = runPlan { inputs with PredecessorPrivate = Known fatPrivate; PredecessorPublic = Known fatPublic; RuntimeBinding = Known(runtimeOf fatPrivate mergeSha); ProjectItems = Known [] }
        expectUnsupported PayloadBound PayloadOverBound ProtocolRelease fat
        Assert.True(plan fatPrivate fatPublic request facts |> Result.isError)

    // ---- toolchain and host ----

    [<Fact>]
    let ``toolchain: a locked content hash that differs from the cache is unsupported, unreadable is unknown`` () =
        Assert.True(isSupported (verdictOf (runPlan (healthy 5)) ToolchainLock))
        expectUnsupported ToolchainLock LockedHashDiffersFromCache ToolchainCache (runPlan { healthy 5 with CachedFSharpCoreHash = Known "other==" })
        expectUnknown ToolchainLock CachedHash Unreadable (runPlan { healthy 5 with CachedFSharpCoreHash = NotObserved Unreadable })
        expectUnknown ToolchainLock LockedHash Unreadable (runPlan { healthy 5 with LockedFSharpCoreHash = NotObserved Unreadable })

    [<Fact>]
    let ``content hashes are read from the lock file entry and the package metadata, and malformed files are unreadable`` () =
        let lockJson = "{\"version\":2,\"dependencies\":{\"net10.0\":{\"FSharp.Core\":{\"type\":\"Direct\",\"resolved\":\"10.1.401\",\"contentHash\":\"abc==\"},\"other\":{\"contentHash\":\"zzz\"}}}}"
        Assert.Equal(Known "abc==", lockedContentHash "FSharp.Core" lockJson)
        Assert.Equal(NotObserved Unreadable, lockedContentHash "Missing.Package" lockJson)
        Assert.Equal(NotObserved Unreadable, lockedContentHash "FSharp.Core" "{")
        Assert.Equal(Known "abc==", metadataContentHash "{\"version\":2,\"contentHash\":\"abc==\",\"source\":\"s\"}")
        Assert.Equal(NotObserved Unreadable, metadataContentHash "{\"version\":2}")
        // The package cache's .nupkg.sha512 is a different digest and is deliberately not the comparison source.
        Assert.Equal(NotObserved Unreadable, metadataContentHash "")

    [<Fact>]
    let ``memory headroom: below the minimum is unsupported, unlimited is supported and unreadable is unknown`` () =
        Assert.True(isSupported (verdictOf (runPlan (healthy 5)) MemoryHeadroom))
        expectUnsupported MemoryHeadroom InsufficientMemoryHeadroom HostCapacity (runPlan { healthy 5 with MemoryHeadroomBytes = Known(Some 1024L) })
        Assert.True(isSupported (verdictOf (runPlan { healthy 5 with MemoryHeadroomBytes = Known None }) MemoryHeadroom))
        expectUnknown MemoryHeadroom CgroupMemory Unreadable (runPlan { healthy 5 with MemoryHeadroomBytes = NotObserved Unreadable })
        Assert.Equal(Known(Some 100L), headroomOf [ "max\n", "5\n"; "1000\n", "900\n" ])
        Assert.Equal(Known None, headroomOf [ "max\n", "5\n" ])
        Assert.Equal(Known(Some 10L), headroomOf [ "100", "90"; "1000", "1" ])
        Assert.Equal(NotObserved Unreadable, headroomOf [])
        Assert.Equal(NotObserved Unreadable, headroomOf [ "garbage", "5" ])

    // ---- Project 3 auto-add and adjacency churn ----

    [<Fact>]
    let ``auto-add: unenrolled items without Status, Track or Work level are unknown (never unsupported) and named; each flips independently`` () =
        let stamp = now - TimeSpan.FromDays 3.0
        let inputs = healthy 5
        let withItems extra = { inputs with ProjectItems = Known(enrolledItems 5 @ extra) }
        // Seeded unenrolled membership is pre-existing and untouched.
        Assert.True(isSupported (verdictOf (runPlan (withItems [ item "FS-GG/.github#4940" "I_unenrolled_a" true true true stamp ])) ProjectAutoAdd))
        for label, status, track, level in [ "Status", false, true, true; "Track", true, false, true; "Work level", true, true, false; "all three", false, false, false ] do
            let report = runPlan (withItems [ item "FS-GG/.github#4940" "I_unenrolled_a" status track level stamp; item "FS-GG/.github#4941" "I_unenrolled_b" true true true stamp ])
            Assert.Equal(Unknown { Input = ProjectItems; Kind = BareMembershipsObserved 1 }, verdictOf report ProjectAutoAdd)
            Assert.Equal((AnyUnknown, 11), (report.Overall, exitCode report.Overall))
            let expected = [ (if status then None else Some "Status"); (if track then None else Some "Track"); (if level then None else Some "Work level") ] |> List.choose id
            Assert.Equal<string list>([ "FS-GG/.github#4940[" + String.Join(",", expected) + "]" ], (finding report ProjectAutoAdd).Subjects)
        // Causal: the same missing-Track item is not an offender once it is enrolled (its node is in the binding).
        let enrolledButBare = item "FS-GG/.github#70001" "I_synthetic_" true false false stamp
        let bareEnrolled = { enrolledButBare with Content = IssueContent("FS-GG/.github#70001", "I_synthetic_" + sentinel + "_0") }
        Assert.True(isSupported (verdictOf (runPlan { inputs with ProjectItems = Known(enrolledItems 5 |> List.tail |> List.append [ bareEnrolled ]) }) ProjectAutoAdd))
        // Non-issue content is named by kind and item id, never by content.
        let draft = { item "x" "n" false false false stamp with Content = OtherContent "DraftIssue" }
        Assert.Equal<string list>([ "DraftIssue:PVTI_n[Status,Track,Work level]" ], (finding (runPlan (withItems [ draft ])) ProjectAutoAdd).Subjects)
        expectUnknown ProjectAutoAdd ProjectItems NotSupplied (runPlan { inputs with ProjectItems = NotObserved NotSupplied })
        expectUnknown ProjectAutoAdd PredecessorPrivate Unreadable (runPlan { inputs with PredecessorPrivate = NotObserved Unreadable })
        // The seven names are capped, never the whole board.
        let many = [ for i in 1 .. 60 -> item $"FS-GG/.github#{9000 + i}" $"I_many_{i}" false false false stamp ]
        Assert.Equal(51, (finding (runPlan (withItems many)) ProjectAutoAdd).Subjects.Length)

    [<Fact>]
    let ``adjacency churn: unrelated item updates in the window are unknown, requested and old updates are not`` () =
        let inputs = healthy 5
        let bind = { inputs with Request = inputs.Request }
        let churnReport extra = run ProgrammeEnrollmentSuccessor BindRuntime { bind with ProjectItems = Known(enrolledItems 5 @ extra) }
        Assert.Equal(SupportedForNextStage "no unrelated Project item updated within the window", verdictOf (churnReport []) AdjacencyChurn)
        let recent = item "FS-GG/.github#7777" "I_other" true true true (now - TimeSpan.FromMinutes 3.0)
        let report = churnReport [ recent ]
        Assert.Equal(Unknown { Input = ProjectItems; Kind = ChurnObserved 1 }, verdictOf report AdjacencyChurn)
        Assert.Equal<string list>([ "FS-GG/.github#7777" ], (finding report AdjacencyChurn).Subjects)
        Assert.Equal((AnyUnknown, 11), (report.Overall, exitCode report.Overall))
        // Causal pair: the same item outside the window, or one of the requested issues, does not count.
        Assert.True(isSupported (verdictOf (churnReport [ { recent with UpdatedAt = now - TimeSpan.FromMinutes 30.0 } ]) AdjacencyChurn))
        let requested = { recent with Content = IssueContent(request.Issues.[0].Ref, "I_new") }
        Assert.True(isSupported (verdictOf (churnReport [ requested ]) AdjacencyChurn))
        // Churn is a property of the next apply, so it is not asked at the planning stages.
        Assert.DoesNotContain(AdjacencyChurn, checksFor ProgrammeEnrollmentSuccessor PlanSuccessor)

    // ---- output hygiene ----

    [<Fact>]
    let ``neither the report nor its json carries sentinel, node id, payload or nonce material`` () =
        let privateJson, _, payload = predecessor 5 ""
        let nonceHex = String.replicate 32 "07"
        let bad = withFact0 (fun f -> { f with Kind = NativePullRequest; NodeId = "I_synthetic_" + sentinel + "_0" }) (healthy 5)
        let report = runPlan { bad with ProjectItems = Known(enrolledItems 5 @ [ item "FS-GG/.github#4940" ("I_" + sentinel) false false false (now - TimeSpan.FromDays 2.0) ]) }
        Assert.Equal(AnyUnsupported, report.Overall)
        // Positive leg: the sentinel and nonce are really in the private inputs.
        Assert.Contains(sentinel, privateJson)
        Assert.Contains(nonceHex, privateJson)
        for text in [ toJson report; sprintf "%A" report ] do
            Assert.DoesNotContain(sentinel, text)
            Assert.DoesNotContain("I_synthetic", text)
            Assert.DoesNotContain(nonceHex, text)
            Assert.DoesNotContain(Convert.ToBase64String payload, text)
        let json = JsonNode.Parse(toJson report)
        Assert.Equal("fsgg.board-v2-preflight/1", json.["schema"].GetValue<string>())
        Assert.Equal("Unsupported", json.["overall"].GetValue<string>())
        Assert.Equal(10, json.["exitCode"].GetValue<int>())
