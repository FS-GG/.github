namespace FS.GG.Coord.Tests

/// Routine-delivery and publication-writer routes, and the PR 3 preflight dispositions. Pure and offline.
module V2RoutesTests =
    open System
    open System.Text.Json.Nodes
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2Enrollment
    open FS.GG.Coord.GitHub.V2Preflight
    open FS.GG.Coord.GitHub.V2NativeResolve
    open FS.GG.Coord.Tests.V2EnrollmentTests

    let private findingOf (report: Report) (check: Check) = report.Findings |> List.find (fun f -> f.Check = check)
    let private verdictOf report check = (findingOf report check).Verdict
    let private isSupported = function SupportedForNextStage _ -> true | _ -> false

    // ---- RoutineDelivery ----

    let private head = String.replicate 40 "a"
    let private baseSha = String.replicate 40 "b"

    let private candidate : RoutineCandidate =
        { BaseRef = "main"; BaseSha = Some baseSha; HeadRef = "routine/coord-synthetic"; HeadSha = head; State = Some "open" }

    let private routine : RoutineInputs =
        { DefaultBranch = "main"; Candidate = Known candidate; LiveBaseSha = Known baseSha; OpenPullRequests = Known 0
          RoutineGate = Known Admitted; ClosingGate = Known NoClosing }

    let private opened = { routine with OpenPullRequests = Known 1 }
    let private withCandidate change (inputs: RoutineInputs) = { inputs with Candidate = Known(change candidate) }
    let private gate kind (inputs: RoutineInputs) = { inputs with RoutineGate = Known(GateFinding kind) }

    [<Fact>]
    let ``routine delivery: healthy open and merge candidates are fully supported with the registered checks`` () =
        let open' = runRoutine OpenPullRequest routine
        Assert.Equal<Check list>(checksFor RoutineDelivery OpenPullRequest, open'.Findings |> List.map _.Check)
        Assert.Equal((AllSupported, 0, 7), (open'.Overall, exitCode open'.Overall, open'.Findings.Length))
        let merge = runRoutine MergePullRequest opened
        Assert.Equal<Check list>(checksFor RoutineDelivery MergePullRequest, merge.Findings |> List.map _.Check)
        Assert.Equal((AllSupported, 0, 10), (merge.Overall, exitCode merge.Overall, merge.Findings.Length))
        // The base ref and the pull request state exist only once there is a pull request: at open they would be tautologies.
        for check in [ BaseIsDefaultBranch; PullRequestOpen; BaseCurrent ] do
            Assert.DoesNotContain(check, checksFor RoutineDelivery OpenPullRequest)
            Assert.Contains(check, checksFor RoutineDelivery MergePullRequest)

    [<Fact>]
    let ``routine delivery: the head branch is matched with the gate's own pattern`` () =
        let branch name = verdictOf (runRoutine OpenPullRequest (withCandidate (fun c -> { c with HeadRef = name }) routine)) RoutineBranch
        let valid = [ "routine/a"; "routine/coord-eff-02c-pr3"; "routine/a.b-c_d"; "routine/0x" ]
        let invalid = [ "routine/"; "routine/Upper"; "routine/-x"; "routine/.x"; "routine/a b"; "routine//x"; "routine/a/b"; "xroutine/a"; "routine"; "feature/x"; "routine/a\n" ]
        for name in valid do Assert.True(isSupported (branch name), name)
        for name in invalid do Assert.Equal(Unsupported(BranchNotRoutine, PullRequestOwner), branch name)
        Assert.Equal((4, 11), (valid.Length, invalid.Length))

    [<Fact>]
    let ``routine delivery: each gate finding decides exactly the check it names, earlier checks passed, later ones were not reached`` () =
        let phases = [ MarkerSingle; MarkerHeadBound; MarkerOperationAllowed; ChangesPresent ]
        let expect kind failing reason supplier =
            let report = runRoutine OpenPullRequest (gate kind routine)
            let at = phases |> List.findIndex ((=) failing)
            phases |> List.iteri (fun i check ->
                let verdict = verdictOf report check
                if i < at then Assert.True(isSupported verdict, kind + " " + checkCode check)
                elif i = at then Assert.Equal(Unsupported(reason, supplier), verdict)
                else Assert.Equal(Unknown { Input = RoutineGate; Kind = NotEvaluated }, verdict))
            Assert.Equal((AnyUnsupported, 10), (report.Overall, exitCode report.Overall))
        expect "routine-missing" MarkerSingle MarkerMissingOrAmbiguous PullRequestOwner
        // The gate refuses a repeated field (`operation=source-change operation=publish`) as ambiguous.
        expect "routine-ambiguous" MarkerSingle MarkerMissingOrAmbiguous PullRequestOwner
        expect "routine-changed-head" MarkerHeadBound MarkerHeadDiffers PullRequestOwner
        expect "routine-protected-operation" MarkerOperationAllowed OperationNotAllowed Root
        expect "routine-empty" ChangesPresent NoChangedPaths PullRequestOwner
        // A finding kind this core does not know is a gap, not a verdict.
        let strange = runRoutine OpenPullRequest (gate "routine-future-kind" routine)
        for check in phases do Assert.Equal(Unknown { Input = RoutineGate; Kind = Unreadable }, verdictOf strange check)
        // Gate not run or failed: unknown, never supported.
        for kind in [ NotSupplied; Unreadable; NotEvaluated ] do
            let report = runRoutine OpenPullRequest { routine with RoutineGate = NotObserved kind }
            for check in phases do Assert.Equal(Unknown { Input = RoutineGate; Kind = kind }, verdictOf report check)
            Assert.Equal(11, exitCode report.Overall)

    [<Fact>]
    let ``routine delivery causal controls: only the gate's finding changes between supported and unsupported`` () =
        let ambiguous = runRoutine OpenPullRequest (gate "routine-ambiguous" routine)
        Assert.Equal(Unsupported(MarkerMissingOrAmbiguous, PullRequestOwner), verdictOf ambiguous MarkerSingle)
        Assert.True(isSupported (verdictOf (runRoutine OpenPullRequest routine) MarkerSingle))
        // The branch alone: the same inputs under a branch outside the pattern.
        Assert.Equal(AnyUnsupported, (runRoutine OpenPullRequest (withCandidate (fun c -> { c with HeadRef = "routine/Upper" }) routine)).Overall)

    [<Fact>]
    let ``routine delivery: pull request facts, state and the single pull request`` () =
        let unsupported check reason supplier report = Assert.Equal(Unsupported(reason, supplier), verdictOf report check)
        unsupported BaseIsDefaultBranch PullRequestBaseNotDefault PullRequestOwner (runRoutine MergePullRequest (withCandidate (fun c -> { c with BaseRef = "release" }) opened))
        unsupported PullRequestOpen PullRequestNotOpen PullRequestOwner (runRoutine MergePullRequest (withCandidate (fun c -> { c with State = Some "closed" }) opened))
        Assert.Equal(Unknown { Input = PullRequestFact; Kind = NotSupplied }, verdictOf (runRoutine MergePullRequest (withCandidate (fun c -> { c with State = None }) opened)) PullRequestOpen)
        Assert.True(isSupported (verdictOf (runRoutine MergePullRequest opened) PullRequestOpen))
        unsupported SinglePullRequest PullRequestAlreadyOpen PullRequestOwner (runRoutine OpenPullRequest opened)
        unsupported SinglePullRequest PullRequestNotSingle PullRequestOwner (runRoutine MergePullRequest routine)
        unsupported SinglePullRequest PullRequestNotSingle PullRequestOwner (runRoutine MergePullRequest { routine with OpenPullRequests = Known 2 })
        Assert.True(isSupported (verdictOf (runRoutine OpenPullRequest routine) SinglePullRequest))
        Assert.True(isSupported (verdictOf (runRoutine MergePullRequest opened) SinglePullRequest))

    [<Fact>]
    let ``routine delivery: the closing-keyword gate result is not re-derived, and an undeclared keyword is unsupported`` () =
        let closing result = verdictOf (runRoutine OpenPullRequest { routine with ClosingGate = result }) ClosingLinkDeclared
        Assert.True(isSupported (closing (Known NoClosing)))
        Assert.Equal(Unsupported(ClosingKeywordUndeclared, PullRequestOwner), closing (Known UndeclaredClosing))
        // A properly declared line is the gate's accepted way to close; whether it fulfils the issue is not decidable here.
        Assert.Equal(Unknown { Input = ClosingGate; Kind = ClosingLinkPresent 2 }, closing (Known(DeclaredClosing [ "#4941"; "#12" ])))
        for kind in [ NotSupplied; Unreadable ] do Assert.Equal(Unknown { Input = ClosingGate; Kind = kind }, closing (NotObserved kind))
        let declared = runRoutine OpenPullRequest { routine with ClosingGate = Known(DeclaredClosing [ "#4941" ]) }
        Assert.Equal<string list>([ "#4941" ], (findingOf declared ClosingLinkDeclared).Subjects)
        Assert.Equal((AnyUnknown, 11), (declared.Overall, exitCode declared.Overall))
        Assert.Equal(10, exitCode (runRoutine OpenPullRequest { routine with ClosingGate = Known UndeclaredClosing }).Overall)

    [<Fact>]
    let ``routine delivery: the base race is unknown and unobserved inputs name themselves`` () =
        let moved = runRoutine MergePullRequest { opened with LiveBaseSha = Known(String.replicate 40 "d") }
        Assert.Equal(Unknown { Input = BaseHead; Kind = BaseMoved }, verdictOf moved BaseCurrent)
        Assert.True(isSupported (verdictOf (runRoutine MergePullRequest opened) BaseCurrent))
        let gap input kind report check = Assert.Equal(Unknown { Input = input; Kind = kind }, verdictOf report check)
        let unread = runRoutine MergePullRequest { opened with Candidate = NotObserved ReadFailed }
        gap PullRequestFact ReadFailed unread BaseIsDefaultBranch
        gap PullRequestFact ReadFailed unread PullRequestOpen
        gap PullRequestFact ReadFailed unread RoutineBranch
        gap PullRequestFact ReadFailed unread BaseCurrent
        gap OpenPullRequestFact ReadFailed (runRoutine OpenPullRequest { routine with OpenPullRequests = NotObserved ReadFailed }) SinglePullRequest
        gap BaseHead ReadFailed (runRoutine MergePullRequest { opened with LiveBaseSha = NotObserved ReadFailed }) BaseCurrent
        // A candidate not yet opened records no base, so the race cannot be assessed.
        gap PullRequestFact NotSupplied (runRoutine MergePullRequest (withCandidate (fun c -> { c with BaseSha = None }) opened)) BaseCurrent
        Assert.Equal(11, exitCode unread.Overall)

    // ---- PublicationWriterRegistration ----

    let private registry =
        """{"schema":1,"gateStep":"Acquire org publication writer lock","writers":[{"repo":"FS-GG/.github","workflows":[".github/workflows/registered.yml"]},{"repo":"FS-GG/Other","workflows":[".github/workflows/elsewhere.yml"]}],"exempt":[{"repo":"FS-GG/.github","workflows":[".github/workflows/exempt.yml"]}]}"""

    let private publishing = "jobs:\n  publish:\n    steps:\n      - run: dotnet nuget push pkg.nupkg\n"
    let private publication path : PublicationInputs =
        { Repository = "FS-GG/.github"; WorkflowPath = path; Workflow = Known publishing; Registry = Known registry; CoverageAudit = Known Clean }

    [<Fact>]
    let ``publication writer: registering an unregistered publishing workflow is supported`` () =
        let report = runPublication RegisterWriter (publication ".github/workflows/new.yml")
        Assert.Equal<Check list>([ WorkflowReadable; WorkflowPublishes; RegistryReadable; NotYetRegistered ], report.Findings |> List.map _.Check)
        Assert.Equal((AllSupported, 0), (report.Overall, exitCode report.Overall))

    [<Fact>]
    let ``publication writer: each registration check has a negative control and a causal pair`` () =
        let fresh = publication ".github/workflows/new.yml"
        let noEffect = runPublication RegisterWriter { fresh with Workflow = Known "jobs:\n  build:\n    steps:\n      - run: dotnet build\n" }
        Assert.Equal(Unsupported(WorkflowHasNoPublicationEffect, WriterRegistry), verdictOf noEffect WorkflowPublishes)
        // Causal: the same text with the effect line commented out is no effect; uncommented it is one.
        let commented = runPublication RegisterWriter { fresh with Workflow = Known "jobs:\n  p:\n    steps:\n      # - run: dotnet nuget push pkg.nupkg\n" }
        Assert.Equal(Unsupported(WorkflowHasNoPublicationEffect, WriterRegistry), verdictOf commented WorkflowPublishes)
        Assert.True(isSupported (verdictOf (runPublication RegisterWriter fresh) WorkflowPublishes))
        for path in [ ".github/workflows/registered.yml"; ".github/workflows/exempt.yml" ] do
            Assert.Equal(Unsupported(WriterAlreadyRegistered, WriterRegistry), verdictOf (runPublication RegisterWriter (publication path)) NotYetRegistered)
        // Registration is per repository: another repository's row does not register this workflow.
        Assert.True(isSupported (verdictOf (runPublication RegisterWriter (publication ".github/workflows/elsewhere.yml")) NotYetRegistered))
        Assert.Equal(Unknown { Input = WorkflowText; Kind = Unreadable }, verdictOf (runPublication RegisterWriter { fresh with Workflow = NotObserved Unreadable }) WorkflowReadable)
        Assert.Equal(Unknown { Input = WriterRegistryText; Kind = Unreadable }, verdictOf (runPublication RegisterWriter { fresh with Registry = Known "{" }) RegistryReadable)
        Assert.Equal(Unknown { Input = WriterRegistryText; Kind = Unreadable }, verdictOf (runPublication RegisterWriter { fresh with Registry = Known """{"schema":2,"writers":[]}""" }) RegistryReadable)
        Assert.Equal(Unknown { Input = WriterRegistryText; Kind = NotSupplied }, verdictOf (runPublication RegisterWriter { fresh with Registry = NotObserved NotSupplied }) NotYetRegistered)

    [<Fact>]
    let ``publication writer: running needs a registered writer and a clean maintained coverage gate`` () =
        let registered = publication ".github/workflows/registered.yml"
        let ok = runPublication RunWriter registered
        Assert.Equal<Check list>([ RegistryReadable; WriterRegistered; CoverageAudit ], ok.Findings |> List.map _.Check)
        Assert.Equal((AllSupported, 0), (ok.Overall, exitCode ok.Overall))
        Assert.Equal(Unsupported(WriterNotRegistered, WriterRegistry), verdictOf (runPublication RunWriter (publication ".github/workflows/new.yml")) WriterRegistered)
        // Exempt is not a writer.
        Assert.Equal(Unsupported(WriterNotRegistered, WriterRegistry), verdictOf (runPublication RunWriter (publication ".github/workflows/exempt.yml")) WriterRegistered)
        let unmet = runPublication RunWriter { registered with CoverageAudit = Known(Unmet 3) }
        Assert.Equal(Unsupported(CoverageUnmet, WriterRegistry), verdictOf unmet CoverageAudit)
        Assert.Equal<string list>([ "3 unmet" ], (findingOf unmet CoverageAudit).Subjects)
        Assert.Equal(10, exitCode unmet.Overall)
        Assert.Equal(Unknown { Input = CoverageGate; Kind = Unreadable }, verdictOf (runPublication RunWriter { registered with CoverageAudit = NotObserved Unreadable }) CoverageAudit)

    // ---- disposition 1: BindRuntime observes the successor ----

    let private healthyBind () =
        let privateJson, publicJson, _ = predecessor 5 ""
        let built = plan privateJson publicJson request facts |> get
        let successorPrivate, successorPublic = V2Projection.prepareProgrammeBinding built.TemplateJson built.Payload (fun () -> Array.create 32 9uy) |> getIo
        privateJson, publicJson, successorPrivate, successorPublic

    let private bindInputs (successorPrivate: string) (successorPublic: string) (privateJson: string) (publicJson: string) : EnrollmentInputs =
        { Request = request; Facts = Known(facts, Live); ConstructorRevision = Known(String.replicate 40 "1"); ProtectedMain = Known(String.replicate 40 "1")
          ProtectedMainFreshness = LiveMain; PredecessorPrivate = Known privateJson; PredecessorPublic = Known publicJson; RuntimeBinding = NotObserved NotSupplied
          SuccessorPrivate = Known successorPrivate; SuccessorMergeSha = Known(String.replicate 40 "5"); SuccessorPublic = Known successorPublic
          InstalledReaderSha256 = Known(String.replicate 64 "a"); KnownInstalledReaders = Set.ofList [ String.replicate 64 "a" ]; KnownLiftedCapReaders = Set.empty; KnownCap500Readers = Set.empty
          LockedFSharpCoreHash = Known "h"; CachedFSharpCoreHash = Known "h"; MemoryHeadroomBytes = Known(Some 4294967296L); HostMemAvailableBytes = Known 8589934592L
          MinimumHeadroomBytes = 1073741824L; PayloadLimitBytes = payloadLimitBytes; BindingLimitBytes = bindingLimitBytes
          ProjectItems = Known [ for i in 0 .. 4 -> { ItemId = $"PVTI_{i}"; Content = IssueContent($"FS-GG/.github#{70001 + i}", $"I_synthetic_{sentinel}_{i}"); HasStatus = true; HasTrack = true; HasWorkLevel = true; RoadmapRaw = None; UpdatedAt = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) } ]
          InvisibleRepositories = Set.empty; Now = DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero); ChurnWindow = TimeSpan.FromMinutes 10.0 }

    [<Fact>]
    let ``BindRuntime decides from the successor binding, merge and public blob, not from the predecessor's`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let report = run ProgrammeEnrollmentSuccessor BindRuntime inputs
        Assert.Equal(AllSupported, report.Overall)
        Assert.DoesNotContain(PredecessorIntegrity, report.Findings |> List.map _.Check)
        Assert.DoesNotContain(RuntimeBindingIntegrity, report.Findings |> List.map _.Check)
        // Causal 1: a predecessor public blob that no longer authenticates is irrelevant to BindRuntime...
        let ruined = { inputs with PredecessorPublic = Known "{}" }
        Assert.Equal(AllSupported, (run ProgrammeEnrollmentSuccessor BindRuntime ruined).Overall)
        // ...and decisive at the planning stage that does read it.
        Assert.Equal(Unsupported(PredecessorCommitmentMismatch, PredecessorCustody), verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor ruined) PredecessorIntegrity)
        // Causal 2: a successor public blob that is not the commitment of the successor binding is decisive at BindRuntime.
        let altered = successorPublic.Replace(successorPublic.Substring(successorPublic.Length - 10, 4), "0000")
        let tampered = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with SuccessorPublic = Known altered }
        Assert.Equal(Unsupported(SuccessorCommitmentMismatch, SuccessorCustody), verdictOf tampered SuccessorIntegrity)
        Assert.Equal(AnyUnsupported, tampered.Overall)
        // A successor that authenticates but is not an extension of the predecessor.
        let otherPrivate, otherPublic, _ = predecessorPadded 5 "" 1
        let unrelated = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with SuccessorPrivate = Known otherPrivate; SuccessorPublic = Known otherPublic }
        Assert.True(isSupported (verdictOf unrelated SuccessorIntegrity))
        Assert.Equal(Unsupported(SuccessorDoesNotExtendPredecessor, SuccessorCustody), verdictOf unrelated SuccessorExtendsPredecessor)

    [<Fact>]
    let ``BindRuntime: each unobserved successor input is an unknown naming that input`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let gap input (changed: EnrollmentInputs) =
            let report = run ProgrammeEnrollmentSuccessor BindRuntime changed
            Assert.Equal(Unknown { Input = input; Kind = NotSupplied }, verdictOf report SuccessorIntegrity)
            Assert.Equal(Unknown { Input = input; Kind = NotSupplied }, verdictOf report SuccessorExtendsPredecessor)
            Assert.Equal((AnyUnknown, 11), (report.Overall, exitCode report.Overall))
        gap SuccessorPrivate { inputs with SuccessorPrivate = NotObserved NotSupplied }
        gap SuccessorMerge { inputs with SuccessorMergeSha = NotObserved NotSupplied }
        gap SuccessorPublic { inputs with SuccessorPublic = NotObserved NotSupplied }
        let badMerge = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with SuccessorMergeSha = Known "not-a-sha" }
        Assert.Equal(Unknown { Input = SuccessorMerge; Kind = Unreadable }, verdictOf badMerge SuccessorIntegrity)

    // ---- disposition 2: ConstructorIdentity freshness ----

    [<Fact>]
    let ``ConstructorIdentity: only a live or asserted main can support or refuse, a local tracking ref is unknown either way`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let differing = { inputs with ConstructorRevision = Known(String.replicate 40 "2") }
        let verdict (changed: EnrollmentInputs) freshness = verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor { changed with ProtectedMainFreshness = freshness }) ConstructorIdentity
        Assert.Equal(Unsupported(ConstructorDiffersFromMain, ConstructorBuild), verdict differing LiveMain)
        Assert.Equal(Unsupported(ConstructorDiffersFromMain, ConstructorBuild), verdict differing AssertedMain)
        // A stale checkout can equal a stale tracking ref, so a match proves nothing offline, and neither does a difference.
        Assert.Equal(Unknown { Input = ProtectedMain; Kind = NotLive }, verdict differing LocalTrackingRef)
        Assert.Equal(Unknown { Input = ProtectedMain; Kind = NotLive }, verdict inputs LocalTrackingRef)
        let local = run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with ProtectedMainFreshness = LocalTrackingRef }
        Assert.Equal(AnyUnknown, (findingOf local ConstructorIdentity |> fun f -> overallOf [ f ]))
        // Equal against a live or asserted main is supported, and the evidence says where the reference came from.
        match verdict inputs LiveMain, verdict inputs AssertedMain with
        | SupportedForNextStage live, SupportedForNextStage asserted ->
            Assert.Contains("live remote head", live)
            Assert.Contains("asserted", asserted)
        | other -> failwithf "%A" other
        Assert.Equal(0, exitCode (run ProgrammeEnrollmentSuccessor BindRuntime { inputs with ProtectedMainFreshness = LiveMain }).Overall)

    // ---- disposition 4: a 404 that may be invisibility ----

    [<Fact>]
    let ``plan blob 404: absence is unsupported only where the repository is visible, otherwise unknown not-visible`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let docRequest = { request with Issues = [ { request.Issues.[1] with Plan = ProtectedDoc("FS-GG/.github", "docs/plan.md") } ] }
        let docFacts = { facts with Issues = [ facts.Issues.[1] ]; PlanBlobs = [ { Repository = "FS-GG/.github"; Path = "docs/plan.md"; OnMain = false } ] }
        let live = { bindInputs successorPrivate successorPublic privateJson publicJson with Request = docRequest; Facts = Known(docFacts, Live) }
        let visible = run ProgrammeEnrollmentSuccessor PlanSuccessor live
        Assert.Equal(Unsupported(PlanBlobAbsentOnMain, PlanAuthor), verdictOf visible PlanBlob)
        // Only the visibility set changes between the two verdicts.
        let hidden = run ProgrammeEnrollmentSuccessor PlanSuccessor { live with InvisibleRepositories = Set.ofList [ "FS-GG/.github" ] }
        Assert.Equal(Unknown { Input = PlanBlobFact; Kind = NotVisible }, verdictOf hidden PlanBlob)
        Assert.Equal<string list>([ docRequest.Issues.[0].Ref ], (findingOf hidden PlanBlob).Subjects)
        Assert.Equal("PlanBlob/NotVisible", gapCode { Input = PlanBlobFact; Kind = NotVisible })
        Assert.Equal((AnyUnknown, 11), (hidden.Overall, exitCode hidden.Overall))

    // ---- disposition 5: the inventory must contain every enrolled node ----

    [<Fact>]
    let ``project inventory completeness: a missing enrolled node makes the auto-add check unknown`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let items = match inputs.ProjectItems with Known list -> list | _ -> []
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor inputs) ProjectAutoAdd))
        let partial = run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with ProjectItems = Known(items |> List.take 3) }
        Assert.Equal(Unknown { Input = ProjectItems; Kind = Incomplete }, verdictOf partial ProjectAutoAdd)
        Assert.Equal<string list>([ "FS-GG/.github#70004"; "FS-GG/.github#70005" ], (findingOf partial ProjectAutoAdd).Subjects)
        // Causal: restoring the two items restores support; nothing else changed.
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with ProjectItems = Known items }) ProjectAutoAdd))
        // An empty inventory is incomplete, not "no bare memberships".
        Assert.Equal(Unknown { Input = ProjectItems; Kind = Incomplete }, verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with ProjectItems = Known [] }) ProjectAutoAdd)
        // Completeness is judged against the predecessor's rows, so BindRuntime does not demand the successor's not-yet-added members.
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime inputs) ProjectAutoAdd))

    // ---- disposition 6: host MemAvailable ----

    [<Fact>]
    let ``memory headroom: with no cgroup limit the host MemAvailable decides`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let memory cgroup host = verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with MemoryHeadroomBytes = cgroup; HostMemAvailableBytes = host }) MemoryHeadroom
        Assert.True(isSupported (memory (Known None) (Known 8589934592L)))
        Assert.Equal(Unsupported(InsufficientMemoryHeadroom, HostCapacity), memory (Known None) (Known 1024L))
        Assert.Equal(Unknown { Input = HostMemory; Kind = Unreadable }, memory (Known None) (NotObserved Unreadable))
        // With a limit, the tighter of the two bounds: a roomy cgroup does not hide a starved host.
        Assert.Equal(Unsupported(InsufficientMemoryHeadroom, HostCapacity), memory (Known(Some 4294967296L)) (Known 1024L))
        Assert.True(isSupported (memory (Known(Some 4294967296L)) (Known 8589934592L)))
        // A known cgroup limit does not bound the host: with MemAvailable unobserved the tighter bound is unknown.
        Assert.Equal(Unknown { Input = HostMemory; Kind = Unreadable }, memory (Known(Some 4294967296L)) (NotObserved Unreadable))
        // An unreadable cgroup stays unknown whatever the host reports: it may be the tighter bound.
        Assert.Equal(Unknown { Input = CgroupMemory; Kind = Unreadable }, memory (NotObserved Unreadable) (Known 8589934592L))

    // ---- disposition 7: a distinct read-failure kind ----

    [<Fact>]
    let ``a failed native read is its own gap kind, distinct from an unreadable file`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let report = run ProgrammeEnrollmentSuccessor PlanSuccessor { inputs with ProjectItems = NotObserved ReadFailed }
        Assert.Equal(Unknown { Input = ProjectItems; Kind = ReadFailed }, verdictOf report ProjectAutoAdd)
        Assert.Equal("ProjectItems/ReadFailed", gapCode { Input = ProjectItems; Kind = ReadFailed })
        Assert.NotEqual<string>(gapCode { Input = ProjectItems; Kind = ReadFailed }, gapCode { Input = ProjectItems; Kind = Unreadable })

    [<Fact>]
    let ``BindRuntime plan references come from the successor's own rows, which the request cannot vouch for`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime inputs) PlanGrammar))
        // A delivered successor whose appended Roadmap is an Outcome issue URL (the cohort 89 r1 failure shape).
        let built = plan privateJson publicJson request facts |> get
        let edited = Text.Encoding.UTF8.GetBytes((Text.Encoding.UTF8.GetString built.Payload).Replace("\"roadmap\":\"https://github.com/FS-GG/.github/issues/3008\"", "\"roadmap\":\"https://github.com/FS-GG/.github/issues/4930\""))
        let badPrivate, badPublic = V2Projection.prepareProgrammeBinding built.TemplateJson edited (fun () -> Array.create 32 9uy) |> getIo
        let bad = { inputs with SuccessorPrivate = Known badPrivate; SuccessorPublic = Known badPublic }
        let atBind = run ProgrammeEnrollmentSuccessor BindRuntime bad
        Assert.Equal(Unsupported(PlanNotAdmitted, PlanAuthor), verdictOf atBind PlanGrammar)
        Assert.Equal<string list>([ "FS-GG/.github#80001" ], (findingOf atBind PlanGrammar).Subjects)
        // The pair is internally authentic: only the successor's own rows reveal the problem...
        Assert.True(isSupported (verdictOf atBind SuccessorIntegrity))
        // ...whereas the request-based planning stage cannot see the successor at all.
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor PlanSuccessor bad) PlanGrammar))

    [<Fact>]
    let ``BindRuntime plan blobs: the appended document must exist on main now, and a hidden repository is not an absence`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime inputs) PlanBlob))
        let absentFacts = { facts with PlanBlobs = [ { Repository = "FS-GG/.github"; Path = planPath; OnMain = false } ] }
        let absent = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with Facts = Known(absentFacts, Live) }
        Assert.Equal(Unsupported(PlanBlobAbsentOnMain, PlanAuthor), verdictOf absent PlanBlob)
        let hidden = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with Facts = Known(absentFacts, Live); InvisibleRepositories = Set.ofList [ "FS-GG/.github" ] }
        Assert.Equal(Unknown { Input = PlanBlobFact; Kind = NotVisible }, verdictOf hidden PlanBlob)
        Assert.Equal(Unknown { Input = NativeFacts; Kind = NotLive }, verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime { inputs with Facts = Known(facts, Supplied) }) PlanBlob)
        Assert.Equal(Unknown { Input = NativeFacts; Kind = Incomplete }, verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime { inputs with Facts = Known({ facts with PlanBlobs = [] }, Live) }) PlanBlob)

    [<Fact>]
    let ``BindRuntime plan blobs: a sha-pinned reference is not checked against main and is reported as not evaluated`` () =
        let privateJson, publicJson, successorPrivate, successorPublic = healthyBind ()
        let inputs = bindInputs successorPrivate successorPublic privateJson publicJson
        let built = plan privateJson publicJson request facts |> get
        let pinned = "https://github.com/FS-GG/.github/blob/" + String.replicate 40 "a" + "/docs/pinned.md"
        let edited = Text.Encoding.UTF8.GetBytes((Text.Encoding.UTF8.GetString built.Payload).Replace("https://github.com/FS-GG/.github/blob/main/" + planPath, pinned))
        Assert.NotEqual<byte array>(built.Payload, edited)
        let pinnedPrivate, pinnedPublic = V2Projection.prepareProgrammeBinding built.TemplateJson edited (fun () -> Array.create 32 9uy) |> getIo
        let report = run ProgrammeEnrollmentSuccessor BindRuntime { inputs with SuccessorPrivate = Known pinnedPrivate; SuccessorPublic = Known pinnedPublic }
        // Not Supported: an immutable reference says nothing about main now, and its text must not claim otherwise.
        Assert.Equal(Unknown { Input = PlanBlobFact; Kind = NotEvaluated }, verdictOf report PlanBlob)
        Assert.Equal<string list>([ request.Issues.[1].Ref ], (findingOf report PlanBlob).Subjects)
        // Causal pair: the same successor with the main reference restored is supported again.
        Assert.True(isSupported (verdictOf (run ProgrammeEnrollmentSuccessor BindRuntime inputs) PlanBlob))
