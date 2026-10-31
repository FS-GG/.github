namespace FS.GG.Coord.Tests

/// Retained, reduced differential: the current planner against the PR 1 planner it replaced (merge a9586e1c),
/// kept as a compiled base copy (`V2EnrollmentPr1Base.fs`, hash-pinned below). This is NOT a proof of equivalence
/// over all inputs; it pins the observed behaviour over a fixed, enumerated scenario set whose size is asserted.
module V2EnrollmentDifferentialTests =
    open System
    open System.Security.Cryptography
    open System.Text
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.Tests.V2EnrollmentTests
    module Base = FS.GG.Coord.Tests.Base.V2EnrollmentPr1

    // ---- the base copy is the PR 1 file, unedited ----

    [<Fact>]
    let ``the retained PR 1 base copy is byte-identical to the merged PR 1 planner`` () =
        let text = IO.File.ReadAllText(IO.Path.Combine(__SOURCE_DIRECTORY__, "V2EnrollmentPr1Base.fs"))
        let marker = "    open FS.GG.Coord.GitHub\n"
        // Undo the one documented edit (the lifted row ceiling) and the file is the PR 1 body again.
        let body = text.Substring(text.IndexOf marker + marker.Length).Replace("let rowCap = V2Projection.maximumProgrammeRows", "let rowCap = 100")
        let digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes body)).ToLowerInvariant()
        Assert.Equal("efe9b8feb3d9dc29085563c05f0ad8dff3fcaf02b2136732cbbb2df8d48d0e10", digest)

    // ---- scenario set ----

    let private codeOf (refusal: V2Enrollment.Refusal) = V2Enrollment.refusalCode refusal

    let private baseFacts (facts: V2Enrollment.NativeFacts) : Base.NativeFacts =
        { ProjectId = facts.ProjectId
          Issues =
            facts.Issues |> List.map (fun f ->
                ({ Ref = f.Ref; NodeId = f.NodeId
                   Kind = (match f.Kind with V2Enrollment.NativeIssue -> Base.NativeIssue | V2Enrollment.NativePullRequest -> Base.NativePullRequest)
                   Repository = f.Repository; ProjectMember = f.ProjectMember; Complete = f.Complete; HumanRoadmap = f.HumanRoadmap }: Base.NativeIssueFact))
          PlanBlobs = facts.PlanBlobs |> List.map (fun b -> ({ Repository = b.Repository; Path = b.Path; OnMain = b.OnMain }: Base.PlanBlobFact)) }

    let private baseRequest (request: V2Enrollment.Request) : Base.Request =
        { Cohort = request.Cohort; PredecessorPrivatePath = request.PredecessorPrivatePath; PredecessorMergeSha = request.PredecessorMergeSha
          Issues =
            request.Issues |> List.map (fun i ->
                ({ Ref = i.Ref; Slice = i.Slice
                   Plan = (match i.Plan with V2Enrollment.ProgrammeIssue3008 -> Base.ProgrammeIssue3008 | V2Enrollment.ProtectedDoc(r, p) -> Base.ProtectedDoc(r, p))
                   Classification = (match i.Classification with V2Enrollment.Outcome -> Base.Outcome | V2Enrollment.Delivery -> Base.Delivery) }: Base.RequestIssue)) }

    let private factMutations : (string * (V2Enrollment.NativeFacts -> V2Enrollment.NativeFacts)) list =
        let issue0 change (facts: V2Enrollment.NativeFacts) = { facts with Issues = change facts.Issues.[0] :: facts.Issues.Tail }
        let issue1 change (facts: V2Enrollment.NativeFacts) = { facts with Issues = facts.Issues.Head :: [ change facts.Issues.[1] ] }
        [ "pr", issue0 (fun f -> { f with Kind = V2Enrollment.NativePullRequest })
          "member", issue1 (fun f -> { f with ProjectMember = false })
          "partial", issue1 (fun f -> { f with Complete = false })
          "node", issue0 (fun f -> { f with NodeId = "X_bad" })
          "blob", (fun facts -> { facts with PlanBlobs = [] })
          "human", issue0 (fun f -> { f with HumanRoadmap = Some "https://github.com/FS-GG/.github/issues/4930" })
          "project", (fun facts -> { facts with ProjectId = "PVT_other" })
          "enrolled", issue0 (fun f -> { f with Ref = "FS-GG/.github#70001"; NodeId = "I_synthetic_" + sentinel + "_0" }) ]

    let private requestMutations : (string * (V2Enrollment.Request -> V2Enrollment.Request)) list =
        [ "duplicate", (fun r -> { r with Issues = r.Issues @ [ r.Issues.[0] ] })
          "outside", (fun r -> { r with Issues = { r.Issues.[0] with Ref = "FS-GG/Other#9" } :: r.Issues.Tail })
          "grammar", (fun r -> { r with Issues = r.Issues.Head :: [ { r.Issues.[1] with Plan = V2Enrollment.ProtectedDoc("FS-GG/.github", "issues/4930") } ] }) ]

    /// Per predecessor shape: the unmutated scenario (1); every single fact mutation and every ordered pair of them, 8 + 8*8 (72);
    /// every single request mutation and each combined with one fact mutation, 3 + 3*8 (27) - these are the 100 PR 2 enumerated;
    /// and, for refusal ORDERING beyond a pair, every unordered triple of fact mutations C(8,3) (56), every request mutation
    /// with an unordered pair of fact mutations 3*C(8,2) (84), every pair of request mutations C(3,2) (3), and each such
    /// request pair with one fact mutation 3*8 (24): 100 + 56 + 84 + 3 + 24 = 267. Then a row that really is already enrolled
    /// (its request ref and fact are the predecessor's first row), alone, with each fact mutation, with each pair of fact
    /// mutations and with each request mutation: 1 + 8 + 28 + 3 = 40, so that AlreadyEnrolled is ordered against the
    /// checks around it. 267 + 40 = 307.
    let private scenarios =
        let facts' = List.indexed factMutations
        let requests' = List.indexed requestMutations
        let combine (fs: (string * (V2Enrollment.NativeFacts -> V2Enrollment.NativeFacts)) list) = (fs |> List.map fst |> String.concat "+"), (fs |> List.map snd |> List.reduce (>>))
        [ yield "none", id, id
          for n1, f1 in factMutations do
            yield n1, id, f1
            for n2, f2 in factMutations do yield n1 + "+" + n2, id, (f1 >> f2)
          for n1, r1 in requestMutations do
            yield n1, r1, id
            for n2, f2 in factMutations do yield n1 + "+" + n2, r1, f2
          // triples of distinct fact mutations, in definition order
          for i, a in facts' do
            for j, b in facts' do
              for k, c in facts' do
                if i < j && j < k then
                    let name, f = combine [ a; b; c ]
                    yield name, id, f
          // one request mutation with two distinct fact mutations
          for n1, r1 in requestMutations do
            for i, a in facts' do
              for j, b in facts' do
                if i < j then
                    let name, f = combine [ a; b ]
                    yield n1 + "+" + name, r1, f
          // pairs of request mutations, alone and with one fact mutation
          for i, (n1, r1) in requests' do
            for j, (n2, r2) in requests' do
              if i < j then
                  yield n1 + "+" + n2, (r1 >> r2), id
                  for n3, f3 in factMutations do yield n1 + "+" + n2 + "+" + n3, (r1 >> r2), f3
          // a genuinely enrolled row: the request names the predecessor's first row, and so does its fact
          let enrolledRequest (r: V2Enrollment.Request) = { r with Issues = { r.Issues.[0] with Ref = "FS-GG/.github#70001" } :: r.Issues.Tail }
          let enrolledFact (f: V2Enrollment.NativeFacts) =
              { f with Issues = { f.Issues.[0] with Ref = "FS-GG/.github#70001"; NodeId = "I_synthetic_" + sentinel + "_0" } :: f.Issues.Tail }
          yield "enrolled-row", enrolledRequest, enrolledFact
          for n1, f1 in factMutations do yield "enrolled-row+" + n1, enrolledRequest, (enrolledFact >> f1)
          for i, (n1, f1) in facts' do
            for j, (n2, f2) in facts' do
              if i < j then yield "enrolled-row+" + n1 + "+" + n2, enrolledRequest, (enrolledFact >> f1 >> f2)
          for n1, r1 in requestMutations do yield "enrolled-row+" + n1, (enrolledRequest >> r1), enrolledFact ]

    /// The ordinary shape, both sides of the old ceiling (98+2, 99+2) and of the lifted one (498+2, 499+2), a non-spliceable payload,
    /// and the lifted cap exceeded together with a non-spliceable payload.
    let private shapes =
        [ "five", 5, ""; "old-cap-edge", 98, ""; "beyond-old-cap", 99, ""; "new-cap-edge", 498, ""; "new-cap-over", 499, ""
          "newline", 5, "\n"; "new-cap-over-newline", 499, "\n" ]

    [<Fact>]
    let ``the scenario set has the stated size`` () =
        Assert.Equal(307, scenarios.Length)
        Assert.Equal(7, shapes.Length)
        Assert.Equal(2149, scenarios.Length * shapes.Length)

    [<Fact>]
    let ``current planner and the PR 1 planner agree on every enumerated scenario`` () =
        let mutable compared = 0
        let mutable accepted = 0
        for shapeName, count, trailing in shapes do
            let privateJson, publicJson, _ = predecessor count trailing
            for name, mutateRequest, mutateFacts in scenarios do
                let r, f = mutateRequest request, mutateFacts facts
                let current = V2Enrollment.plan privateJson publicJson r f
                let baseline = Base.plan privateJson publicJson (baseRequest r) (baseFacts f)
                compared <- compared + 1
                match current, baseline with
                | Ok c, Ok b ->
                    accepted <- accepted + 1
                    Assert.True((c.Payload = b.Payload && c.TemplateJson = b.TemplateJson && c.TotalRows = b.TotalRows), $"{shapeName}/{name}: accepted outputs differ")
                | Error c, Error b -> Assert.True((codeOf c = Base.refusalCode b), $"{shapeName}/{name}: {codeOf c} vs {Base.refusalCode b}")
                | Ok _, Error b -> failwithf "%s/%s: current accepts, PR 1 refuses %s" shapeName name (Base.refusalCode b)
                | Error c, Ok _ -> failwithf "%s/%s: current refuses %s, PR 1 accepts" shapeName name (codeOf c)
        Assert.Equal(2149, compared)
        // Non-vacuity: the set exercises both outcomes (accepted: the unmutated scenario on the four shapes within the ceiling).
        Assert.Equal(4, accepted)
