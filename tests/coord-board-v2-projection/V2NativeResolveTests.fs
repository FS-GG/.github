namespace FS.GG.Coord.Tests

/// Online native-fact resolution controls over a recording fake transport. No network; synthetic identities only.
module V2NativeResolveTests =
    open System
    open System.Text.Json.Nodes
    open Xunit
    open FS.GG.Coord.GitHub
    open FS.GG.Coord.GitHub.V2NativeResolve
    open FS.GG.Coord.GitHub.V2Enrollment
    open FS.GG.Coord.Tests.V2EnrollmentTests

    let private binding () =
        let privateJson, publicJson, _ = predecessor 5 ""
        privateJson, publicJson, (match V2Projection.decodeBinding privateJson with Ok value -> value | Error _ -> failwith "binding")

    let private fieldValue (name: string) (kind: string) (value: string option) : string =
        match value with
        | None -> ""
        | Some text when kind = "text" -> $"{{\"id\":1,\"name\":\"{name}\",\"data_type\":\"text\",\"value\":{{\"html\":\"<a>x</a>\",\"raw\":\"{text}\"}}}}"
        | Some text -> $"{{\"id\":2,\"name\":\"{name}\",\"data_type\":\"single_select\",\"value\":{{\"id\":\"o\",\"name\":{{\"html\":\"{text}\",\"raw\":\"{text}\"}}}}}}"

    let private itemJson (node: string) (number: int) (repository: string) (status: bool) (track: bool) (level: bool) (roadmap: string option) (updated: string) =
        let fields =
            [ fieldValue "Status" "single_select" (if status then Some "Backlog" else None)
              fieldValue "Track" "single_select" (if track then Some "Active delivery" else None)
              fieldValue "Work level" "single_select" (if level then Some "Delivery" else None)
              fieldValue "Roadmap" "text" roadmap ] |> List.filter (fun x -> x <> "") |> String.concat ","
        $"{{\"node_id\":\"PVTI_{node}\",\"content_type\":\"Issue\",\"updated_at\":\"{updated}\",\"content\":{{\"node_id\":\"{node}\",\"number\":{number},\"repository_url\":\"https://api.github.com/repos/{repository}\"}},\"fields\":[{fields}]}}"

    type private Fake =
        { Calls: ResizeArray<string * bool>
          Get: Get }

    /// `overrides` replaces the canned response for a path prefix.
    let private fake (items: string list) (overrides: (string * GetResult) list) : Fake =
        let calls = ResizeArray<string * bool>()
        let canned (path: string) (paginated: bool) =
            // A prefix matches by StartsWith; one written "=path" matches that path exactly.
            match overrides |> List.tryFind (fun (prefix, _) -> if prefix.StartsWith "=" then path = prefix.Substring 1 else path.StartsWith prefix) with
            | Some(_, result) -> result
            | None ->
                if path = "/orgs/FS-GG/projectsV2/3" then Body $"{{\"number\":3,\"node_id\":\"{projectId}\"}}"
                elif path.StartsWith "/orgs/FS-GG/projectsV2/3/fields" then
                    Body "[[{\"id\":10,\"name\":\"Status\"},{\"id\":11,\"name\":\"Roadmap\"},{\"id\":12,\"name\":\"Track\"},{\"id\":13,\"name\":\"Work level\"},{\"id\":14,\"name\":\"Title\"}]]"
                elif path.StartsWith "/orgs/FS-GG/projectsV2/3/items" then Body("[[" + String.concat "," items + "]]")
                elif path = "/repos/FS-GG/.github" then Body "{\"private\":false}"
                elif path = "/repos/FS-GG/.github/issues/80001" then Body "{\"node_id\":\"I_synthetic_new1\",\"repository_url\":\"https://api.github.com/repos/FS-GG/.github\"}"
                elif path = "/repos/FS-GG/FS.GG.Templates/issues/80002" then Body "{\"node_id\":\"I_synthetic_new2\",\"repository_url\":\"https://api.github.com/repos/FS-GG/FS.GG.Templates\"}"
                elif path.StartsWith("/repos/FS-GG/.github/contents/" + planPath) then Body "{\"type\":\"file\",\"name\":\"synthetic-plan.md\"}"
                else NotFound
        { Calls = calls; Get = fun path paginated -> calls.Add((path, paginated)); canned path paginated }

    let private seeded = [ itemJson "I_synthetic_new1" 80001 "FS-GG/.github" true true true (Some "https://github.com/FS-GG/.github/issues/3008") "2026-10-30T11:00:00Z"
                           itemJson "I_synthetic_new2" 80002 "FS-GG/FS.GG.Templates" true true true None "2026-10-30T11:00:00Z" ]

    [<Fact>]
    let ``resolve emits the native-facts document the planner strictly decodes and plans from`` () =
        let privateJson, publicJson, binding = binding ()
        let transport = fake seeded []
        let resolved = resolve transport.Get binding request |> get
        let json = encodeNativeFacts resolved.Facts
        let decoded = decodeNativeFacts json |> get
        Assert.Equal(resolved.Facts, decoded)
        Assert.Equal(projectId, decoded.ProjectId)
        Assert.Equal<string list>([ "I_synthetic_new1"; "I_synthetic_new2" ], decoded.Issues |> List.map _.NodeId)
        Assert.True(decoded.Issues |> List.forall (fun f -> f.Kind = NativeIssue && f.ProjectMember && f.Complete))
        Assert.Equal(Some "https://github.com/FS-GG/.github/issues/3008", decoded.Issues.[0].HumanRoadmap)
        Assert.Equal(None, decoded.Issues.[1].HumanRoadmap)
        Assert.Equal<bool list>([ true ], decoded.PlanBlobs |> List.map _.OnMain)
        // End to end: the resolved facts are accepted by the planner.
        let built = plan privateJson publicJson request decoded |> get
        Assert.Equal(7, built.TotalRows)
        Assert.Equal(2, resolved.Items.Length)
        Assert.Equal<string>("FS-GG/.github#80001", (match resolved.Items.[0].Content with IssueContent(reference, _) -> reference | _ -> ""))

    [<Fact>]
    let ``every request is a read-only GET on the closed grammar, and the argument vector can express nothing else`` () =
        let _, _, binding = binding ()
        let transport = fake seeded []
        resolve transport.Get binding request |> get |> ignore
        Assert.Equal(6, transport.Calls.Count)
        for path, paginated in transport.Calls do
            let arguments = getArguments path paginated |> Option.defaultWith (fun () -> failwithf "outside the GET grammar: %s" path)
            Assert.Equal<string list>([ "api"; "--method"; "GET" ], arguments |> List.take 3)
            Assert.Equal(path, List.last arguments)
            for forbidden in [ "-f"; "-F"; "--field"; "--raw-field"; "--input"; "-X"; "POST"; "PATCH"; "PUT"; "DELETE"; "graphql" ] do
                Assert.DoesNotContain(forbidden, arguments)
            Assert.Equal(paginated, List.contains "--paginate" arguments)
        // Paths outside the grammar have no argument vector at all.
        for refused in [ "/graphql"; "graphql"; "/user"; "https://api.github.com/repos/a/b"; "/repos/a/../b"; "/repos/a/b c"; "/repos/a/b;rm"; "/repos/a/b?x=$(id)"; ""; "/orgs/" ] do
            Assert.True((getArguments refused false).IsNone, refused)

    [<Fact>]
    let ``an absent issue is partial visibility and the planner refuses it with its own code`` () =
        let privateJson, publicJson, binding = binding ()
        let transport = fake seeded [ "/repos/FS-GG/.github/issues/80001", NotFound ]
        let resolved = resolve transport.Get binding request |> get
        Assert.False(resolved.Facts.Issues.[0].Complete)
        Assert.Equal(Error PartialVisibility, plan privateJson publicJson request (decodeNativeFacts (encodeNativeFacts resolved.Facts) |> get) |> Result.map ignore)

    [<Fact>]
    let ``kind, membership, human roadmap and blob existence come from native state`` () =
        let _, _, binding = binding ()
        let transport =
            fake [ itemJson "I_synthetic_new2" 80002 "FS-GG/FS.GG.Templates" true true true None "2026-10-30T11:00:00Z" ]
                 [ "/repos/FS-GG/.github/issues/80001", Body "{\"node_id\":\"I_pr\",\"pull_request\":{},\"repository_url\":\"https://api.github.com/repos/FS-GG/.github\"}"
                   "/repos/FS-GG/.github/contents/", NotFound ]
        let resolved = resolve transport.Get binding request |> get
        Assert.Equal(NativePullRequest, resolved.Facts.Issues.[0].Kind)
        Assert.False(resolved.Facts.Issues.[0].ProjectMember)
        Assert.True(resolved.Facts.Issues.[1].ProjectMember)
        Assert.False(resolved.Facts.PlanBlobs.[0].OnMain)

    [<Fact>]
    let ``a failed or unusable read returns NativeReadFailed and no facts, never an echo of the response`` () =
        let _, _, binding = binding ()
        let sentinelBody = Body("{\"leak\":\"" + sentinel + "\"")
        for label, overrides in
            [ "issue failed", [ "/repos/FS-GG/.github/issues/80001", Failed ]
              "issue malformed", [ "/repos/FS-GG/.github/issues/80001", sentinelBody ]
              "project failed", [ "/orgs/FS-GG/projectsV2/3/items", Failed ]
              "project malformed", [ "/orgs/FS-GG/projectsV2/3/items", sentinelBody ]
              "fields missing", [ "/orgs/FS-GG/projectsV2/3/fields", Body "[[{\"id\":10,\"name\":\"Status\"}]]" ]
              "wrong project", [ "/orgs/FS-GG/projectsV2/3", Body $"{{\"number\":4,\"node_id\":\"{projectId}\"}}" ]
              "blob failed", [ "/repos/FS-GG/.github/contents/", Failed ] ] do
            let result = resolve (fake seeded overrides).Get binding request
            Assert.Equal(Error NativeReadFailed, result |> Result.map ignore)
            Assert.DoesNotContain(sentinel, sprintf "%A" result)
            ignore label

    [<Fact>]
    let ``scheduling field presence is read per item and an unset or empty value counts as missing`` () =
        let _, _, binding = binding ()
        let bare = "{\"node_id\":\"PVTI_bare\",\"content_type\":\"Issue\",\"updated_at\":\"2026-10-30T11:00:00Z\",\"content\":{\"node_id\":\"I_bare\",\"number\":4940,\"repository_url\":\"https://api.github.com/repos/FS-GG/.github\"},\"fields\":[{\"id\":9,\"name\":\"Title\",\"value\":{\"raw\":\"t\"}}]}"
        let nulled = "{\"node_id\":\"PVTI_null\",\"content_type\":\"Issue\",\"updated_at\":\"2026-10-30T11:00:00Z\",\"content\":{\"node_id\":\"I_null\",\"number\":4941,\"repository_url\":\"https://api.github.com/repos/FS-GG/.github\"},\"fields\":[{\"id\":1,\"name\":\"Status\",\"value\":null}]}"
        let draft = "{\"node_id\":\"PVTI_draft\",\"content_type\":\"DraftIssue\",\"updated_at\":\"2026-10-30T11:00:00Z\",\"content\":{},\"fields\":[]}"
        let resolved = resolve (fake (seeded @ [ bare; nulled; draft ]) []).Get binding request |> get
        let byId = resolved.Items |> List.map (fun i -> i.ItemId, i) |> Map.ofList
        Assert.Equal((false, false, false), (byId.["PVTI_bare"].HasStatus, byId.["PVTI_bare"].HasTrack, byId.["PVTI_bare"].HasWorkLevel))
        Assert.False(byId.["PVTI_null"].HasStatus)
        Assert.Equal((true, true, true), (byId.["PVTI_I_synthetic_new1"].HasStatus, byId.["PVTI_I_synthetic_new1"].HasTrack, byId.["PVTI_I_synthetic_new1"].HasWorkLevel))
        Assert.Equal(OtherContent "DraftIssue", byId.["PVTI_draft"].Content)

    // ---- PR 3: per-endpoint allowlist ----

    let private allowedEndpoints =
        [ "/orgs/FS-GG/projectsV2/3", false
          "/orgs/FS-GG/projectsV2/3/fields?per_page=100", true
          "/orgs/FS-GG/projectsV2/3/items?per_page=100&fields=10,11,12,13", true
          "/repos/FS-GG/.github", false
          "/repos/FS-GG/.github/issues/80001", false
          "/repos/FS-GG/.github/pulls/4956", false
          "/repos/FS-GG/.github/pulls?state=open&per_page=100", true
          "/repos/FS-GG/.github/git/ref/heads/main", false
          "/repos/FS-GG/.github/contents/docs/roadmaps/plan.md?ref=main", false ]

    [<Fact>]
    let ``the allowlist admits exactly the nine endpoints the resolvers use`` () =
        Assert.Equal(9, allowedEndpoints.Length)
        for path, paginated in allowedEndpoints do
            Assert.True((getArguments path paginated).IsSome, path)
            // Pagination must match what the endpoint needs, in both directions.
            Assert.True((getArguments path (not paginated)).IsNone, path + " with the other pagination")

    [<Fact>]
    let ``anything off the allowlist has no argument vector, including every near miss`` () =
        let refused =
            [ // not an allowlisted endpoint, although inside /repos or /orgs
              "/repos/FS-GG/.github/issues/80001/comments"; "/repos/FS-GG/.github/issues"; "/repos/FS-GG/.github/pulls/4956/files"
              "/repos/FS-GG/.github/git/ref/heads/other"; "/repos/FS-GG/.github/git/ref/tags/v1"; "/repos/FS-GG/.github/actions/runs"
              "/orgs/FS-GG/members"; "/orgs/FS-GG/projectsV2"; "/orgs/FS-GG/projectsV2/3/items"; "/orgs/FS-GG/projectsV2/3/views"
              // query near misses
              "/repos/FS-GG/.github/contents/docs/x.md"; "/repos/FS-GG/.github/contents/docs/x.md?ref=other"; "/repos/FS-GG/.github/contents/docs/x.md?ref=main&x=1"
              "/orgs/FS-GG/projectsV2/3/fields?per_page=1"; "/orgs/FS-GG/projectsV2/3/items?per_page=100&fields=ab"; "/orgs/FS-GG/projectsV2/3/items?fields=10&per_page=100"
              "/repos/FS-GG/.github/pulls?state=all&per_page=100"; "/repos/FS-GG/.github/pulls?state=open"
              // percent-encoding is gone from segments
              "/repos/FS-GG/.github/contents/docs%2Fx.md?ref=main"; "/repos/FS-GG/.github/contents/docs/x%2e%2e/y.md?ref=main"; "/repos/FS-GG%2F.github"
              // traversal segments
              "/repos/FS-GG/.github/contents/../x.md?ref=main"; "/repos/FS-GG/.github/contents/./x.md?ref=main"; "/repos/FS-GG/../issues/1"; "/repos/../.github/issues/1"
              // empty segments, bad numbers, nonsense
              "/repos/FS-GG//issues/1"; "/repos/FS-GG/.github/issues/0"; "/repos/FS-GG/.github/issues/01"; "/repos/FS-GG/.github/issues/-1"; "/repos/FS-GG/.github/issues/1/"
              "/graphql"; "graphql"; "/user"; "https://api.github.com/repos/a/b"; ""; "/repos/FS-GG/.github/contents/a/b/c/d/e/f/g/h/i/j/k/l/m.md?ref=main" ]
        for path in refused do
            Assert.True((getArguments path false).IsNone && (getArguments path true).IsNone, path)

    [<Fact>]
    let ``a name containing two dots is a name; only a whole traversal segment is refused`` () =
        // The PR 2 grammar rejected any ".." substring, so a legitimate document name was unreachable.
        Assert.True((getArguments "/repos/FS-GG/.github/contents/docs/a..b.md?ref=main" false).IsSome)
        Assert.True((getArguments "/repos/FS-GG/.github/contents/docs/v1..2/notes..md?ref=main" false).IsSome)
        Assert.True((getArguments "/repos/FS-GG/.github/contents/docs/..hidden?ref=main" false).IsSome)
        // Causal pair: the same position as a whole segment is a traversal and is refused.
        Assert.True((getArguments "/repos/FS-GG/.github/contents/docs/../hidden?ref=main" false).IsNone)
        Assert.True((getArguments "/repos/FS-GG/.github/contents/docs/./hidden?ref=main" false).IsNone)

    // ---- PR 3: a 404 may be invisibility ----

    [<Fact>]
    let ``a plan document 404 in a readable repository is absence, and in an unreadable one it is reported as hidden`` () =
        let _, _, binding = binding ()
        let missing = "/repos/FS-GG/.github/contents/"
        // Readable repository: the repository GET succeeds, so the 404 is an absence.
        let absent = resolve (fake seeded [ missing, NotFound; "=/repos/FS-GG/.github", Body "{\"private\":false}" ]).Get binding request |> get
        Assert.False(absent.Facts.PlanBlobs.[0].OnMain)
        Assert.Empty(absent.HiddenRepositories)
        // Unreadable repository: the very same 404 on the document, but the repository itself is 404 too.
        let transport = fake seeded [ missing, NotFound; "=/repos/FS-GG/.github", NotFound ]
        let hidden = resolve transport.Get binding request |> get
        Assert.False(hidden.Facts.PlanBlobs.[0].OnMain)
        Assert.Equal<string list>([ "FS-GG/.github" ], hidden.HiddenRepositories)
        // The facts document keeps its fixed schema: visibility is not smuggled into it.
        Assert.DoesNotContain("hidden", encodeNativeFacts hidden.Facts, StringComparison.OrdinalIgnoreCase)
        Assert.Contains(("/repos/FS-GG/.github", false), transport.Calls)
        // A failed repository read is a failed read, not an absence.
        Assert.Equal(Error NativeReadFailed, resolve (fake seeded [ missing, NotFound; "=/repos/FS-GG/.github", Failed ]).Get binding request |> Result.map ignore)

    // ---- PR 3: main head, pull requests ----

    [<Fact>]
    let ``main head and pull requests are read through the allowlisted GETs only`` () =
        let sha = String.replicate 40 "e"
        let calls = ResizeArray<string * bool>()
        let pull (number: int) (headRef: string) =
            $"{{\"number\":{number},\"state\":\"open\",\"title\":\"t\",\"body\":null,\"base\":{{\"ref\":\"main\",\"sha\":\"{sha}\"}},\"head\":{{\"ref\":\"{headRef}\",\"sha\":\"{sha}\"}}}}"
        let transport : Get =
            fun path paginated ->
                calls.Add((path, paginated))
                if path = "/repos/FS-GG/.github/git/ref/heads/main" then Body $"{{\"object\":{{\"sha\":\"{sha}\"}}}}"
                elif path = "/repos/FS-GG/.github/pulls/7" then Body(pull 7 "routine/a")
                elif path = "/repos/FS-GG/.github/pulls?state=open&per_page=100" then Body("[[" + pull 7 "routine/a" + "," + pull 8 "routine/b" + "],[" + pull 9 "routine/a" + "]]")
                else NotFound
        Assert.Equal(Ok sha, readMainHead transport "FS-GG/.github")
        let one = readPullRequest transport "FS-GG/.github" 7 |> get
        Assert.Equal(("main", sha, "routine/a", ""), (one.BaseRef, one.BaseSha, one.HeadRef, one.Body))
        Assert.Equal<int list>([ 7; 9 ], openPullRequestsFor transport "FS-GG/.github" "routine/a" |> get |> List.map _.Number)
        for path, paginated in calls do Assert.True((getArguments path paginated).IsSome, path)
        // A sha that is not 40 hex, a missing pull request and a bad slug are all NativeReadFailed.
        Assert.Equal(Error NativeReadFailed, readMainHead (fun _ _ -> Body "{\"object\":{\"sha\":\"abc\"}}") "FS-GG/.github")
        Assert.Equal(Error NativeReadFailed, readPullRequest transport "FS-GG/.github" 99 |> Result.map ignore)
        Assert.Equal(Error NativeReadFailed, readMainHead transport "not a slug")
