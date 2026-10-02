module FS.GG.Coord.GitHub.Tests.V2ProjectionTests

open System
open System.Collections.Generic
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Errors
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V2Projection

let private ok body =
    Ok
        {
            Status = 200
            Body = body
            ETag = None
            NextLink = None
            Headers = Map.empty
        }

let private binding =
    {
        BindingVersion = 2
        ImportRecipeRevision = String.replicate 40 "e"
        ImportArtifactSha256 = String.replicate 64 "f"
        SelectedIssues = Map.ofList [ "I_native", "FS-GG/.github#2963"; "I_2964", "FS-GG/.github#2964"; "I_2965", "FS-GG/.github#2965" ]
        SchemaVersion = 1
        RecipeRevision = String.replicate 40 "a"
        PopulationRevision = String.replicate 40 "c"
        OrganizationId = "O_fsgg"
        ArtifactSha256 = String.replicate 64 "b"
        OwnerKind = OwnerKind.Org
        Owner = "FS-GG"
        ProjectNumber = 77
        ProjectTitle = "Coordination V2"
        ProjectId = "PVT_coord_v2"
        Status =
            {
                Id = "PVTSSF_status_v2"
                Options =
                    Map.ofList
                        [ "Backlog", "status_backlog"
                          "Ready", "status_ready"
                          "In progress", "status_wip"
                          "Blocked", "status_blocked"
                          "Done", "status_done" ]
            }
        RoadmapFieldId = "PVTF_roadmap_v2"
        Track =
            {
                Id = "PVTSSF_track_v2"
                Options = Map.ofList [ "Active delivery", "track_active"; "Follow-up", "track_followup" ]
            }
        Observation =
            {
                Id = "PVTSSF_observation_v2"
                Options = Map.ofList [ "Verified", "observation_verified"; "Stale", "observation_stale"; "Unknown", "observation_unknown" ]
            }
        Repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/FS.GG.Coordination" ]
    }

let private issue =
    {
        Owner = "FS-GG"
        Repository = ".github"
        Number = 2963
    }

let private request = { Issue = issue; ExpectedNodeId = "I_native" }

let private verified revision : SourceVerifier =
    fun actual ->
        Assert.Equal(issue, actual)
        Ok(Current revision)

let private exactProject projectId observationFieldId =
    $"""{{"data":{{"organization":{{"login":"FS-GG","projectV2":{{"id":"{projectId}","number":77,"title":"Coordination V2","fields":{{"totalCount":4,"nodes":[
      {{"id":"PVTSSF_status_v2","name":"Status","dataType":"SINGLE_SELECT","options":[{{"id":"status_backlog","name":"Backlog"}},{{"id":"status_ready","name":"Ready"}},{{"id":"status_wip","name":"In progress"}},{{"id":"status_blocked","name":"Blocked"}},{{"id":"status_done","name":"Done"}}]}},
      {{"id":"PVTF_roadmap_v2","name":"Roadmap","dataType":"TEXT"}},
      {{"id":"PVTSSF_track_v2","name":"Track","dataType":"SINGLE_SELECT","options":[{{"id":"track_active","name":"Active delivery"}},{{"id":"track_followup","name":"Follow-up"}}]}},
      {{"id":"{observationFieldId}","name":"Observation","dataType":"SINGLE_SELECT","options":[{{"id":"observation_verified","name":"Verified"}},{{"id":"observation_stale","name":"Stale"}},{{"id":"observation_unknown","name":"Unknown"}}]}}
    ]}}}}}}}}}}"""

let private organization = ok """{"data":{"organization":{"id":"O_fsgg","login":"FS-GG"}}}"""

let private target = ok (exactProject binding.ProjectId binding.Observation.Id)

let private membershipFor projectId contentId =
    ok $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","issue":{{"id":"I_native","number":2963,"projectItems":{{"totalCount":1,"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[{{"id":"PVTI_2963","project":{{"id":"{projectId}"}},"content":{{"id":"{contentId}","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}]}}}}}}}}}}"""

let private membership = membershipFor binding.ProjectId "I_native"

let private observation value =
    let field = value |> Option.map (fun name -> $"{{\"name\":\"{name}\"}}") |> Option.defaultValue "null"
    ok $"""{{"data":{{"node":{{"id":"PVTI_2963","project":{{"id":"PVT_coord_v2"}},"content":{{"id":"I_native","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}},"fieldValueByName":{field}}}}}}}"""

let private mutationSuccess =
    ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

let private scripted (responses: IoResult<Response> list) =
    let queue = Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then failwith "the adapter exceeded its bounded fixture"
        else queue.Dequeue())

[<Fact>]
let ``one shot writes only the reviewed Observation field`` () =
    let transport = scripted [ organization; target; membership; observation (Some "Stale"); mutationSuccess; observation (Some "Verified") ]
    let mutable sourceChecks = 0
    let verifier: SourceVerifier =
        fun actual ->
            sourceChecks <- sourceChecks + 1
            Assert.Equal(issue, actual)
            Ok(Current "issue-etag-1")

    match runOneShot verifier transport binding request with
    | Ok report ->
        Assert.Equal(2, sourceChecks)
        Assert.Equal(Updated "PVTI_2963", report.Outcome)
        Assert.Equal(2, report.SourceChecks)
        Assert.Equal(5, report.ProjectReads)
        Assert.Equal(1, report.Mutations)
        Assert.Equal(6, transport.GraphQlCalls)
        Assert.Single(transport.Mutations) |> ignore
        Assert.True(transport.Logged "--field-id PVTSSF_observation_v2")
        Assert.True(transport.Logged "--single-select-option-id observation_verified")
        Assert.False(transport.Logged "PVTSSF_status_v2")
        Assert.False(transport.Logged "PVTF_roadmap_v2")
        Assert.False(transport.Logged "PVTSSF_track_v2")
    | other -> failwith $"expected one owned projection write, got %A{other}"

[<Fact>]
let ``duplicate retry re-reads and emits no second mutation`` () =
    let transport =
        scripted
            [ organization; target; membership; observation None; mutationSuccess; observation (Some "Verified")
              organization; target; membership; observation (Some "Verified") ]

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Ok { Outcome = Updated _ } -> ()
    | other -> failwith $"first pass must update, got %A{other}"

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Ok report ->
        Assert.Equal(AlreadyCurrent "PVTI_2963", report.Outcome)
        Assert.Equal(0, report.Mutations)
        Assert.Single(transport.Mutations) |> ignore
    | other -> failwith $"retry must converge without a duplicate mutation, got %A{other}"

[<Fact>]
let ``lost response remains unknown and retry first resolves live state`` () =
    let transport =
        scripted
            [ organization; target; membership; observation None; Error(Transport "response lost")
              organization; target; membership; observation (Some "Verified") ]

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Error(Transport "response lost") -> ()
    | other -> failwith $"a lost response must not be reported applied, got %A{other}"

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Ok { Outcome = AlreadyCurrent "PVTI_2963"; Mutations = 0 } ->
        Assert.Single(transport.Mutations) |> ignore
    | other -> failwith $"retry must read the applied value before considering another mutation, got %A{other}"

[<Fact>]
let ``wrong target field drift and denied access all fail before mutation`` () =
    let cases =
        [ [ ok (exactProject "PVT_wrong" binding.Observation.Id) ]
          [ ok (exactProject binding.ProjectId "PVTSSF_observation_drift") ]
          [ ok """{"errors":[{"type":"FORBIDDEN","message":"Resource not accessible"}],"data":{"organization":null}}""" ] ]

    for responses in cases do
        let transport = scripted (organization :: responses)

        match runOneShot (verified "issue-etag-1") transport binding request with
        | Error _ -> Assert.Empty(transport.Mutations)
        | Ok report -> failwith $"unsafe target read produced a projection report: %A{report}"

[<Fact>]
let ``incomplete membership pagination cannot become absence or license a mutation`` () =
    let incomplete =
        ok """{"data":{"repository":{"nameWithOwner":"FS-GG/.github","issue":{"id":"I_native","number":2963,"projectItems":{"totalCount":51,"nodes":[]}}}}}"""

    let transport = scripted [ organization; target; incomplete ]

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Error(Malformed _) ->
        Assert.Equal(3, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"an incomplete connection must fail closed, got %A{other}"

[<Fact>]
let ``source verifier false result refuses before target IO`` () =
    let transport = scripted []
    let verifier: SourceVerifier = fun _ -> Ok(Refused "canonical evidence did not validate")

    match runOneShot verifier transport binding request with
    | Error(Http(403, message)) ->
        Assert.Contains("verification refused", message)
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"a false source verification must refuse, got %A{other}"

[<Fact>]
let ``source verifier stale result refuses before target IO`` () =
    let transport = scripted []
    let verifier: SourceVerifier = fun _ -> Ok(Stale(Some "issue-etag-0"))

    match runOneShot verifier transport binding request with
    | Error(Http(409, message)) ->
        Assert.Contains("stale", message)
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"stale source evidence must refuse, got %A{other}"

[<Fact>]
let ``lost source verifier response remains an error and cannot reach target IO`` () =
    let transport = scripted []
    let verifier: SourceVerifier = fun _ -> Error(Transport "canonical source response lost")

    match runOneShot verifier transport binding request with
    | Error(Transport "canonical source response lost") ->
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"a lost source-verifier response must remain unknown, got %A{other}"

[<Fact>]
let ``foreign repository refuses before invoking source verifier`` () =
    let transport = scripted []
    let mutable called = false
    let verifier: SourceVerifier = fun _ -> called <- true; Ok(Current "issue-etag-1")
    let candidate = { request with Issue = { issue with Repository = "foreign" } }

    match runOneShot verifier transport binding candidate with
    | Error(Http(403, _)) ->
        Assert.False(called)
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"a foreign repository must refuse, got %A{other}"

[<Fact>]
let ``legacy exact Project 1 cannot be supplied as a V2 binding`` () =
    let legacy =
        {
            binding with
                ProjectNumber = 1
                ProjectTitle = "Coordination"
                ProjectId = "PVT_kwDOEYAWY84Bb08W"
        }

    match validateBinding legacy with
    | Error(Http(422, message)) -> Assert.Contains("legacy", message)
    | other -> failwith $"legacy Project 1 must retain its separate meaning, got %A{other}"

[<Fact>]
let ``an unknown live Observation option fails without rewriting it`` () =
    let transport = scripted [ organization; target; membership; observation (Some "Surprising") ]

    match runOneShot (verified "issue-etag-1") transport binding request with
    | Error(Malformed _) -> Assert.Empty(transport.Mutations)
    | other -> failwith $"an unknown live value must refuse, got %A{other}"

[<Fact>]
let ``same number foreign project never licenses an Observation read`` () =
    let transport = scripted [ organization; target; membershipFor "PVT_foreign_same_number_77" "I_native" ]
    match runOneShot (verified "revision") transport binding request with
    | Error(NotFound _) -> Assert.Equal(3, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
    | other -> failwith $"foreign project must refuse: {other}"

[<Fact>]
let ``mismatched membership content refuses before Observation`` () =
    let transport = scripted [ organization; target; membershipFor binding.ProjectId "I_other_issue" ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Malformed _) -> Assert.Equal(3, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
    | other -> failwith $"foreign content must refuse: {other}"

[<Fact>]
let ``successful mutation with failed readback retains unknown outcome`` () =
    let transport = scripted [ organization; target; membership; observation None; mutationSuccess; Error(Transport "readback lost") ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Transport "readback lost") -> Assert.Single(transport.Mutations) |> ignore
    | other -> failwith $"unreadback mutation cannot be applied evidence: {other}"

[<Fact>]
let ``item changing projects between membership and field read refuses`` () =
    let foreign = observation None |> Result.map (fun response -> { response with Body = response.Body.Replace("PVT_coord_v2", "PVT_foreign") })
    let transport = scripted [ organization; target; membership; foreign ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Malformed _) -> Assert.Empty(transport.Mutations)
    | other -> failwith $"field read must verify target again: {other}"

let private canonicalBinding =
    use stream = IO.File.OpenRead typeof<Binding>.Assembly.Location
    { binding with ArtifactSha256 = Security.Cryptography.SHA256.HashData stream |> Convert.ToHexStringLower }

let private protectedBlob value =
    let encoded = Text.Json.JsonSerializer.Serialize(value: string)
    ok $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","ref":{{"target":{{"oid":"{canonicalBinding.PopulationRevision}"}}}},"object":{{"oid":"{String.replicate 40 "b"}","text":{encoded}}},"current":{{"oid":"{String.replicate 40 "b"}","text":{encoded}}}}}}}}}"""

let private canonicalManifest =
    let rows =
        [ 2963; 2964; 2965 ] |> List.map (fun number ->
            $"""{{"issue":"FS-GG/.github#{number}","nodeId":"{if number = 2963 then "I_native" else $"I_{number}"}","observedUpdatedAt":"2026-10-02T00:00:00Z","observedState":"open","decision":"import","adjudication":"verified-remaining","pilot":true,"roadmap":"docs/github-substrate-v2-roadmap.md","dependencies":[]}}""") |> String.concat ","
    $"""{{"schema":"fsgg.coordination-board-v2-import/v1","target":{{"ownerKind":"organization","owner":"FS-GG","title":"Coordination V2","id":"PVT_coord_v2","number":77,"creationState":"created-and-read-back"}},"binding":{{"organizationId":"O_fsgg","visibility":"complete","authorization":"root-selected","recipeRevision":"{canonicalBinding.ImportRecipeRevision}","artifactSha256":"{canonicalBinding.ImportArtifactSha256}","repositories":["FS-GG/.github","FS-GG/FS.GG.Coordination"]}},"items":[{rows}]}}"""

let private native number updated =
    let nodeId = if number = 2963 then "I_native" else $"I_{number}"
    ok $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","issue":{{"id":"{nodeId}","number":{number},"updatedAt":"{updated}","state":"OPEN","url":"https://github.com/FS-GG/.github/issues/{number}","body":"canonical plan", "repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}}}}}"""

[<Fact>]
let ``fixed composition covers successful stale and denied members independently`` () =
    let transport = scripted
                        [ protectedBlob canonicalManifest
                          native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
                          organization; target; membership; observation (Some "Verified")
                          native 2964 "2026-10-02T01:00:00Z"
                          Error(Http(403, "source denied")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.Equal(Some 3, report.Selected)
    Assert.Equal(3, report.Attempted)
    Assert.Equal(1, report.Verified)
    Assert.Equal<string list>([ "Verified"; "Stale"; "Unknown" ], report.Items |> List.map (fun item -> item.Health))
    Assert.Equal(Some 1, report.Items[0].MembershipPages)
    Assert.True(report.Items[0].LastVerified.IsSome)
    Assert.All(report.Items, fun item -> Assert.StartsWith("Unknown:", item.NativeAcceptance))
    Assert.Empty(transport.Mutations)
    Assert.True(report.PopulationGap.IsNone)

[<Fact>]
let ``loaded artifact mismatch refuses before source IO`` () =
    let transport = scripted []
    let report = V2ProjectionSource.runFixed transport binding None
    Assert.True(report.PopulationGap.IsSome)
    Assert.Equal(None, report.Selected)
    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``unadjudicated protected population cannot assert Current`` () =
    let manifest = canonicalManifest.Replace("verified-remaining", "unknown")
    let transport = scripted [ protectedBlob manifest ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.True(report.PopulationGap.IsSome)
    Assert.Equal(None, report.Selected)
    Assert.Equal(1, transport.GraphQlCalls)
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``lost protected population response preserves prior last verified evidence`` () =
    let transport = scripted
                        [ protectedBlob canonicalManifest
                          native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
                          organization; target; membership; observation (Some "Verified")
                          native 2964 "2026-10-02T01:00:00Z"; Error(Http(403, "source denied")) ]
    let previous = V2ProjectionSource.runFixed transport canonicalBinding None
    let retry = scripted [ Error(Transport "population response lost") ]
    let report = V2ProjectionSource.runFixed retry canonicalBinding (Some previous)
    Assert.Equal(None, report.Selected)
    Assert.Equal(0, report.Attempted)
    Assert.Equal(0, report.Verified)
    Assert.Equal(previous.Items[0].LastVerified, report.Items[0].LastVerified)
    Assert.True(report.Items[0].Projection.IsNone)
    Assert.Equal("Unknown", report.Items[0].Health)
    Assert.Empty(retry.Mutations)

[<Fact>]
let ``human owned field option drift is never repaired by refresh`` () =
    for option in [ "status_ready"; "track_active"; "observation_verified" ] do
        let drifted = target |> Result.map (fun response -> { response with Body = response.Body.Replace(option, "changed_identity") })
        let transport = scripted [ organization; drifted ]
        match runOneShot (verified "revision") transport binding request with
        | Error(Malformed _) -> Assert.Empty(transport.Mutations)
        | other -> failwith $"schema drift must refuse: {other}"

[<Fact>]
let ``source snapshot changing before field mutation preserves human and owned values`` () =
    let mutable calls = 0
    let verifier: SourceVerifier = fun _ -> calls <- calls + 1; Ok(Current $"revision-{calls}")
    let transport = scripted [ organization; target; membership; observation None ]
    match runOneShot verifier transport binding request with
    | Error(Http(409, _)) -> Assert.Empty(transport.Mutations)
    | other -> failwith $"changed source cannot license write: {other}"

[<Fact>]
let ``membership page ceiling remains Unknown rather than absent`` () =
    let page index =
        let body =
            $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","issue":{{"id":"I_native","number":2963,"projectItems":{{"totalCount":11,"pageInfo":{{"hasNextPage":true,"endCursor":"cursor-{index}"}},"nodes":[{{"id":"foreign-item-{index}","project":{{"id":"foreign-project-{index}","number":77}},"content":{{"id":"I_native","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}]}}}}}}}}}}"""
        ok body
    let transport = scripted ([ organization; target ] @ ([ 1..10 ] |> List.map page))
    match runOneShot (verified "revision") transport binding request with
    | Error(Malformed _) -> Assert.Equal(12, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
    | other -> failwith $"bounded incomplete read cannot prove absence: {other}"

[<Fact>]
let ``partial field population refuses before item access`` () =
    let incomplete = target |> Result.map (fun response -> { response with Body = response.Body.Replace("totalCount\":4", "totalCount\":5") })
    let transport = scripted [ organization; incomplete ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Malformed _) -> Assert.Equal(2, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
    | other -> failwith $"partial field map cannot license item read: {other}"

[<Fact>]
let ``repository bindings cannot project each other's source`` () =
    let first = { binding with Repositories = Set.singleton "FS-GG/.github" }
    let second = { binding with ProjectId = "PVT_product_other"; Repositories = Set.singleton "FS-GG/FS.GG.Coordination" }
    for selected, candidate in [ first, { request with Issue = { issue with Repository = "FS.GG.Coordination" } }; second, request ] do
        let transport = scripted []
        match runOneShot (verified "revision") transport selected candidate with
        | Error(Http(403, _)) -> Assert.Equal(0, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
        | other -> failwith $"distinct scope must refuse: {other}"

[<Fact>]
let ``candidate commit population is refused independently of its asserted metadata`` () =
    let candidate = protectedBlob canonicalManifest |> Result.map (fun response -> { response with Body = response.Body.Replace("\"current\":{\"oid\":\"" + String.replicate 40 "b", "\"current\":{\"oid\":\"" + String.replicate 40 "d") })
    let transport = scripted [ candidate ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.True(report.PopulationGap.IsSome)
    Assert.Equal(None, report.Selected)
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``fixed composition preserves lost field result and retry verifies live state`` () =
    let initial = scripted
                      [ protectedBlob canonicalManifest
                        native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
                        organization; target; membership; observation None
                        protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
                        Error(Transport "field response lost")
                        native 2964 "2026-10-02T01:00:00Z"; Error(Http(403, "source denied")) ]
    let previous = V2ProjectionSource.runFixed initial canonicalBinding None
    Assert.Equal(Some 3, previous.Selected)
    Assert.Equal(1, previous.Items[0].MutationAttempts)
    Assert.Equal("Unknown", previous.Items[0].Health)
    Assert.True(previous.Items[0].Projection.IsNone)
    Assert.True(previous.Items[0].LastVerified.IsNone)
    let retry = scripted
                    [ protectedBlob canonicalManifest
                      native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
                      organization; target; membership; observation (Some "Verified")
                      native 2964 "2026-10-02T01:00:00Z"; Error(Http(403, "source denied")) ]
    let report = V2ProjectionSource.runFixed retry canonicalBinding (Some previous)
    Assert.Equal(Some 3, report.Selected)
    Assert.Equal(3, report.Attempted)
    Assert.Equal(1, report.Verified)
    Assert.Equal(0, report.Items[0].MutationAttempts)
    Assert.True(report.Items[0].LastVerified.IsSome)
    Assert.Empty(retry.Mutations)

[<Fact>]
let ``native membership identity must equal reviewed canonical issue node`` () =
    let foreign = membership |> Result.map (fun response -> { response with Body = response.Body.Replace("I_native", "I_recreated") })
    let transport = scripted [ organization; target; foreign ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Malformed _) -> Assert.Equal(3, transport.GraphQlCalls); Assert.Empty(transport.Mutations)
    | other -> failwith $"native node drift cannot reach Observation: {other}"

[<Fact>]
let ``unrelated protected main advance preserves the pinned population blob`` () =
    let advanced = protectedBlob canonicalManifest |> Result.map (fun response -> { response with Body = response.Body.Replace(canonicalBinding.PopulationRevision, String.replicate 40 "d") })
    let transport = scripted [ advanced; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"; organization; target; membership; observation (Some "Verified"); Error(Http(403, "second")); Error(Http(403, "third")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.True(report.PopulationGap.IsNone)
    Assert.Equal(1, report.Verified)
    Assert.True(report.Items.Head.DependencyReadComplete)
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``programme3008 is read natively and records revision and body digest`` () =
    let manifest = canonicalManifest.Replace("docs/github-substrate-v2-roadmap.md", "https://github.com/FS-GG/.github/issues/3008")
    let transport = scripted [ protectedBlob manifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; native 3008 "2026-10-02T02:00:00Z"; organization; target; membership; observation (Some "Verified"); Error(Http(403, "second")); Error(Http(403, "third")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.Equal(1, report.Verified)
    let plan = report.Items.Head.PlanObservation.Value
    Assert.Equal(3008, plan.Issue.Number)
    Assert.Equal("2026-10-02T02:00:00Z", plan.UpdatedAt)
    Assert.Equal(64, plan.BodySha256.Length)
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``new native dependency against reviewed empty snapshot is stale without writes`` () =
    let dependency = """[{"node_id":"I_blocker","number":12,"updated_at":"2026-10-02T02:00:00Z","state":"open","html_url":"https://github.com/FS-GG/.github/issues/12"}]"""
    let transport = scripted [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; ok dependency; Error(Http(403, "second")); Error(Http(403, "third")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.Equal("Stale", report.Items.Head.Health)
    Assert.True(report.Items.Head.DependencyReadComplete)
    Assert.Single(report.Items.Head.DependencyObservations) |> ignore
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``partial native dependency snapshot remains unknown without writes`` () =
    let partial = ok "[]" |> Result.map (fun response -> { response with NextLink = Some "https://api.github.com/unreviewed-page" })
    let transport = scripted [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; partial; Error(Http(403, "second")); Error(Http(403, "third")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.Equal("Unknown", report.Items.Head.Health)
    Assert.False(report.Items.Head.DependencyReadComplete)
    Assert.Empty(transport.Mutations)

[<Fact>]
let ``programme body or revision drift during source recheck prevents dispatch`` () =
    for changed in [ native 3008 "2026-10-02T02:01:00Z"; native 3008 "2026-10-02T02:00:00Z" |> Result.map (fun response -> { response with Body = response.Body.Replace("canonical plan", "changed plan") }) ] do
        let manifest = canonicalManifest.Replace("docs/github-substrate-v2-roadmap.md", "https://github.com/FS-GG/.github/issues/3008")
        let transport = scripted [ protectedBlob manifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; native 3008 "2026-10-02T02:00:00Z"; organization; target; membership; observation None; protectedBlob manifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; changed; Error(Http(403, "second")); Error(Http(403, "third")) ]
        let report = V2ProjectionSource.runFixed transport canonicalBinding None
        Assert.Equal(0, report.Verified)
        Assert.Equal(0, report.Items.Head.MutationAttempts)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``foreign programme and pull request URLs cannot supply owning evidence`` () =
    for url in [ "https://github.com/foreign/repo/issues/3008"; "https://github.com/FS-GG/.github/pull/3008"; "https://example.org/arbitrary" ] do
        let manifest = canonicalManifest.Replace("docs/github-substrate-v2-roadmap.md", url)
        let transport = scripted [ protectedBlob manifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; Error(Http(403, "second")); Error(Http(403, "third")) ]
        let report = V2ProjectionSource.runFixed transport canonicalBinding None
        Assert.Equal("Unknown", report.Items.Head.Health)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``import constructor provenance mismatch never reaches native reads or writes`` () =
    for manifest in [ canonicalManifest.Replace(canonicalBinding.ImportArtifactSha256, String.replicate 64 "1"); canonicalManifest.Replace(canonicalBinding.ImportRecipeRevision, String.replicate 40 "2") ] do
        let transport = scripted [ protectedBlob manifest ]
        let report = V2ProjectionSource.runFixed transport canonicalBinding None
        Assert.True(report.PopulationGap.IsSome)
        Assert.Equal(None, report.Selected)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``protected population movement before dispatch retains prior field without write`` () =
    let changed = protectedBlob canonicalManifest |> Result.map (fun response -> { response with Body = response.Body.Replace("\"current\":{\"oid\":\"" + String.replicate 40 "b", "\"current\":{\"oid\":\"" + String.replicate 40 "d") })
    let transport = scripted [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"; organization; target; membership; observation None; changed; Error(Http(403, "second")); Error(Http(403, "third")) ]
    let report = V2ProjectionSource.runFixed transport canonicalBinding None
    Assert.Equal(0, report.Items.Head.MutationAttempts)
    Assert.Equal("Unknown", report.Items.Head.Health)
    Assert.Empty(transport.Mutations)
