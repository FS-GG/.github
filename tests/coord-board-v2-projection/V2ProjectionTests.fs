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
        WorkLevel = None; ProgrammeEnrollment = None
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
    ok $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","issue":{{"id":"I_native","number":2963,"projectItems":{{"totalCount":1,"pageInfo":{{"hasNextPage":false,"endCursor":null}},"nodes":[{{"id":"PVTI_2963","isArchived":false,"project":{{"id":"{projectId}"}},"content":{{"id":"{contentId}","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}]}}}}}}}}}}"""

let private membership = membershipFor binding.ProjectId "I_native"

let private observation value =
    let field = value |> Option.map (fun name -> $"{{\"name\":\"{name}\"}}") |> Option.defaultValue "null"
    ok $"""{{"data":{{"node":{{"id":"PVTI_2963","project":{{"id":"PVT_coord_v2"}},"content":{{"id":"I_native","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}},"fieldValueByName":{field}}}}}}}"""

let private mutationSuccess =
    ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

let private scripted (responses: IoResult<Transport.Response> list) =
    let queue = Queue<IoResult<Transport.Response>>(responses)

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
let ``an archived board card is reported and never written`` () =
    // #5119: Done retention archives cards; an archived enrolled card is an explicit refusal before any write.
    let archived = ok ((match membership with Ok value -> value.Body | Error _ -> failwith "fixture").Replace("\"isArchived\":false", "\"isArchived\":true"))
    let transport = scripted [ organization; target; archived; observation (Some "Stale"); mutationSuccess ]
    match runOneShot (verified "revision") transport binding request with
    | Error(Http(409, message)) ->
        Assert.Contains("archived (Done retention)", message)
        Assert.Empty(transport.Mutations)
    | other -> failwithf "expected an explicit archived-card refusal, got %A" other

let private archivedCohort () =
    let rows = [ 1 .. 3 ] |> List.map (fun number -> $"I_arch_{number}", { ProgrammeFixture.issue with Number = 7000 + number }, "docs/coordination/programme.md")
    let selected = ProgrammeFixture.enroll { ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList } rows
    rows, selected

let private archivedInspection (archive: bool) (unlist: bool) =
    let rows, selected = archivedCohort ()
    let change (request: Transport.Request) (result: IoResult<Transport.Response>) =
        result |> Result.map (fun (response: Transport.Response) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
            if archive && request.Subject = "bound V2 successor planning memberships" then
                let node = root.["data"].["repository"].["issue"].["projectItems"].["nodes"].[0]
                if node.["content"].["id"].GetValue<string>() = "I_arch_2" then node.["isArchived"] <- System.Text.Json.Nodes.JsonValue.Create true
            elif unlist && request.Subject = "complete V2 project memberships" then
                let items = root.["data"].["node"].["items"]
                let nodes = items.["nodes"].AsArray()
                for index in nodes.Count - 1 .. -1 .. 0 do
                    if nodes.[index].["content"].["id"].GetValue<string>() = "I_arch_2" then nodes.RemoveAt index
                items.["totalCount"] <- System.Text.Json.Nodes.JsonValue.Create nodes.Count
            { response with Body = root.ToJsonString() })
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    Assert.Empty reads.Mutations
    report

[<Fact>]
let ``an enrolled row whose card is archived is an explicit archived membership, never a missing row`` () =
    let report = archivedInspection true true
    Assert.True(report.MembershipGap.IsNone, string report.MembershipGap)
    Assert.True(report.Evidence.PopulationGap.IsNone, string report.Evidence.PopulationGap)
    let archived = report.Memberships |> List.filter _.Archived
    Assert.Equal<string list>([ "I_arch_2" ], archived |> List.map _.ContentId)
    Assert.True(archived.Head.Enrolled)
    Assert.Equal<string list>([ "I_arch_1"; "I_arch_2"; "I_arch_3" ], report.Memberships |> List.filter _.Enrolled |> List.map _.ContentId |> List.sort)
    for item in report.Items do
        let flagged = item.Discrepancies |> List.contains V2ProjectionSource.archivedCardDiscrepancy
        Assert.True((flagged = (item.ExpectedNodeId = "I_arch_2")), item.ExpectedNodeId)
        Assert.True(item.Planning.Value.Archived = (item.ExpectedNodeId = "I_arch_2"))

[<Fact>]
let ``an enrolled row missing from the complete membership, or an archived card still listed, is a membership gap`` () =
    for archive, unlist in [ false, true; true, false ] do
        let report = archivedInspection archive unlist
        let gap = report.MembershipGap |> Option.map Errors.explain |> Option.defaultValue ""
        Assert.Contains("membership cross-check", gap)
        Assert.Contains("1 enrolled row(s) disagree", gap)
        Assert.True(report.Evidence.PopulationGap.IsSome)
        Assert.Empty(report.Memberships |> List.filter _.Archived)
    let clean = archivedInspection false false
    Assert.True(clean.MembershipGap.IsNone, string clean.MembershipGap)
    Assert.Empty(clean.Items |> List.collect _.Discrepancies |> List.filter ((=) V2ProjectionSource.archivedCardDiscrepancy))

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
            $"""{{"data":{{"repository":{{"nameWithOwner":"FS-GG/.github","issue":{{"id":"I_native","number":2963,"projectItems":{{"totalCount":11,"pageInfo":{{"hasNextPage":true,"endCursor":"cursor-{index}"}},"nodes":[{{"id":"foreign-item-{index}","isArchived":false,"project":{{"id":"foreign-project-{index}","number":77}},"content":{{"id":"I_native","number":2963,"repository":{{"nameWithOwner":"FS-GG/.github"}}}}}}]}}}}}}}}}}"""
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

let private planningValues =
    ok """{"data":{"node":{"id":"PVTI_2963","project":{"id":"PVT_coord_v2"},"content":{"id":"I_native","number":2963,"repository":{"nameWithOwner":"FS-GG/.github"}},"status":{"__typename":"ProjectV2ItemFieldSingleSelectValue","name":"Ready","optionId":"status_ready","field":{"id":"PVTSSF_status_v2"}},"roadmap":{"__typename":"ProjectV2ItemFieldTextValue","text":"docs/github-substrate-v2-roadmap.md","field":{"id":"PVTF_roadmap_v2"}},"track":{"__typename":"ProjectV2ItemFieldSingleSelectValue","name":"Active delivery","optionId":"track_active","field":{"id":"PVTSSF_track_v2"}},"observation":{"__typename":"ProjectV2ItemFieldSingleSelectValue","name":"Unknown","optionId":"observation_unknown","field":{"id":"PVTSSF_observation_v2"}}}}}"""

[<Fact>]
let ``planning inspection retains human fields without mutation or acceptance`` () =
    let transport = scripted [ organization; target; membership; planningValues ]
    let observed = readPlanning transport binding request |> Result.defaultWith (Errors.explain >> failwith)
    Assert.Equal(Some "Ready", observed.Status)
    Assert.Equal(Some "Active delivery", observed.Track)
    Assert.Equal(Some "Unknown", observed.Observation)
    Assert.Empty transport.Mutations

[<Fact>]
let ``planning inspection refuses foreign content field and option identities`` () =
    for oldValue, newValue in [ "\"content\":{\"id\":\"I_native\"", "\"content\":{\"id\":\"I_foreign\""; "PVTSSF_status_v2", "PVTSSF_foreign"; "status_ready", "foreign_option"; "PVT_coord_v2", "PVT_foreign" ] do
        let changed = planningValues |> Result.map (fun response -> { response with Body = response.Body.Replace(oldValue, newValue) })
        let transport = scripted [ organization; target; membership; changed ]
        Assert.True(readPlanning transport binding request |> Result.isError)
        Assert.Empty transport.Mutations

let private inspectionResponses firstNative dependencies planning =
    [ protectedBlob canonicalManifest; firstNative; dependencies; protectedBlob "canonical owning roadmap"
      organization; target; membership; planning
      Error(Http(403, "second native")); Error(Http(403, "second planning"))
      Error(Http(403, "third native")); Error(Http(403, "third planning")) ]

[<Fact>]
let ``inspection reads current source but never upgrades Unknown or dispatches candidates`` () =
    let transport = scripted (inspectionResponses (native 2963 "2026-10-02T00:00:00Z") (ok "[]") planningValues)
    let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
    Assert.Equal(Some 3, report.Evidence.Selected)
    Assert.Equal(3, report.Evidence.Attempted)
    let item = report.Items.Head
    Assert.Equal("Current", item.SourceCurrentness)
    Assert.True(item.Evidence.DependencyReadComplete)
    Assert.Empty item.Evidence.DependencyObservations
    Assert.Equal("Unknown", item.Evidence.Health)
    Assert.Equal(Some "Ready", item.Planning |> Option.bind _.Status)
    Assert.True(item.Evidence.Projection.IsNone)
    Assert.True(item.Evidence.LastVerified.IsNone)
    Assert.All(report.Items, fun row -> Assert.Equal(0, row.Evidence.MutationAttempts))
    Assert.Empty transport.Mutations
    let facts: V2ProjectionSource.IntegratorFacts = { OpenPullRequests = None; TouchSets = None; AvailableSlots = None }
    let candidates = V2ProjectionSource.planningCandidates report facts
    Assert.Equal(3, candidates.Length)
    Assert.Contains("Current capacity is unknown", candidates.Head.UnmetOrUnknown)
    Assert.True((candidates = V2ProjectionSource.planningCandidates report facts))
    Assert.DoesNotContain(report.Items, fun row -> row.Evidence.Issue.Number = 935)

[<Fact>]
let ``inspection incomplete population is Unknown and yields no candidates`` () =
    let transport = scripted [ Error(Http(403, "population unavailable")) ]
    let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
    Assert.True(report.Evidence.Selected.IsNone)
    Assert.True(report.Evidence.PopulationGap.IsSome)
    Assert.Equal(DateTimeOffset.MinValue, report.NativeIntegrator.ObservedAt)
    Assert.Empty report.Items
    let facts: V2ProjectionSource.IntegratorFacts = { OpenPullRequests = Some []; TouchSets = Some Map.empty; AvailableSlots = Some 1 }
    Assert.Empty(V2ProjectionSource.planningCandidates report facts)
    Assert.Empty transport.Mutations

[<Fact>]
let ``inspection closed native and Done retain acceptance discrepancy`` () =
    let closed = native 2963 "2026-10-02T00:00:00Z" |> Result.map (fun response -> { response with Body = response.Body.Replace("OPEN", "CLOSED") })
    let donePlanning = planningValues |> Result.map (fun response -> { response with Body = response.Body.Replace("Ready", "Done").Replace("status_ready", "status_done") })
    let transport = scripted [ protectedBlob canonicalManifest; closed; organization; target; membership; donePlanning; Error(Http(403, "second")); Error(Http(403, "second planning")); Error(Http(403, "third")); Error(Http(403, "third planning")) ]
    let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
    Assert.Equal("Stale", report.Items.Head.SourceCurrentness)
    Assert.Equal(Some "Done", report.Items.Head.Planning |> Option.bind _.Status)
    Assert.Contains(report.Items.Head.Discrepancies, fun value -> value.Contains("closed"))
    Assert.Contains(report.Items.Head.Discrepancies, fun value -> value.Contains("Done"))
    Assert.StartsWith("Unknown", report.Items.Head.Evidence.NativeAcceptance)
    Assert.Empty transport.Mutations

[<Fact>]
let ``inspection changed issue dependency and access denial remain zero effect`` () =
    let denied = Error(Http(403, "denied"))
    let newDependency = ok """[{"id":9,"node_id":"I_blocker","number":9,"state":"open","updated_at":"2026-10-02T01:00:00Z","html_url":"https://github.com/FS-GG/.github/issues/9","body":"blocker"}]"""
    for responses, expected in
        [ [ protectedBlob canonicalManifest; native 2963 "2026-10-02T01:00:00Z"; organization; target; membership; planningValues; denied; denied; denied; denied ], "Stale"
          [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; newDependency; organization; target; membership; planningValues; denied; denied; denied; denied ], "Stale"
          [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; denied; organization; target; membership; planningValues; denied; denied; denied; denied ], "Unknown" ] do
        let transport = scripted responses
        let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
        Assert.Equal(expected, report.Items.Head.SourceCurrentness)
        Assert.Empty transport.Mutations
        Assert.True(report.Items.Head.Evidence.Projection.IsNone)

[<Fact>]
let ``fresh three selected issues inspect Current while closed omitted history stays outside the exact map`` () =
    let history = """,{"issue":"FS-GG/.github#3009","nodeId":"I_3009","observedUpdatedAt":"2026-10-04T13:04:38Z","observedState":"closed","decision":"omit-delivered","adjudication":"unknown","pilot":false,"remainingOutcome":null,"roadmap":"docs/github-substrate-v2-roadmap.md","dependencies":[]}"""
    let manifest = canonicalManifest.Substring(0, canonicalManifest.Length - 2) + history + "]}"
    let memberResponse number (response: IoResult<Transport.Response>) =
        response |> Result.map (fun value ->
            { value with Body = value.Body.Replace("I_native", (if number = 2963 then "I_native" else $"I_{number}")).Replace("2963", string number) })
    let reads number =
        [ native number "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "canonical owning roadmap"
          organization; target; memberResponse number membership; memberResponse number planningValues ]
    let transport = scripted (protectedBlob manifest :: ([ 2963; 2964; 2965 ] |> List.collect reads))
    let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
    Assert.Equal(Some 3, report.Evidence.Selected)
    Assert.Equal(3, report.Evidence.Attempted)
    Assert.True(report.Evidence.PopulationGap.IsNone)
    Assert.All(report.Items, fun item ->
        Assert.Equal("Current", item.SourceCurrentness)
        Assert.True(item.Evidence.DependencyReadComplete)
        Assert.True(item.Planning.IsSome)
        Assert.Equal(0, item.Evidence.MutationAttempts))
    Assert.DoesNotContain(report.Items, fun item -> item.Evidence.Issue.Number = 3009)
    Assert.Empty transport.Mutations
    // Neither the historical four-target map nor a missing selected member can match.
    for wrong in [ { canonicalBinding with SelectedIssues = canonicalBinding.SelectedIssues.Add("I_3009", "FS-GG/.github#3009") }
                   { canonicalBinding with SelectedIssues = canonicalBinding.SelectedIssues.Remove("I_2965") } ] do
        let refused = scripted [ protectedBlob manifest ]
        let result = V2ProjectionSource.inspectFixed refused wrong None
        Assert.True(result.Evidence.PopulationGap.IsSome)
        Assert.Equal(None, result.Evidence.Selected)
        Assert.Equal(0, result.Evidence.Attempted)
        Assert.Empty refused.Mutations

[<Fact>]
let ``inspection admits fourth issue only by exact selected population and rejects unbound child`` () =
    let fourthBinding = { canonicalBinding with SelectedIssues = canonicalBinding.SelectedIssues.Add("I_3009", "FS-GG/.github#3009") }
    let fourthRow = """,{"issue":"FS-GG/.github#3009","nodeId":"I_3009","observedUpdatedAt":"2026-10-02T00:00:00Z","observedState":"open","decision":"import","adjudication":"verified-remaining","pilot":true,"roadmap":"docs/github-substrate-v2-roadmap.md","dependencies":[]}"""
    let manifest = canonicalManifest.Substring(0, canonicalManifest.Length - 2) + fourthRow + "]}"
    let denied = Error(Http(403, "unavailable current read"))
    let transport = scripted (protectedBlob manifest :: List.replicate 8 denied)
    let report = V2ProjectionSource.inspectFixed transport fourthBinding None
    Assert.Equal(Some 4, report.Evidence.Selected)
    Assert.Contains(report.Items, fun item -> item.Evidence.Issue.Number = 3009 && item.ExpectedNodeId = "I_3009")
    Assert.DoesNotContain(report.Items, fun item -> item.Evidence.Issue.Number = 935)
    let foreign = scripted [ protectedBlob manifest ]
    let refused = V2ProjectionSource.inspectFixed foreign { fourthBinding with SelectedIssues = fourthBinding.SelectedIssues.Remove("I_3009").Add("I_935", "FS-GG/.github#935") } None
    Assert.True(refused.Evidence.PopulationGap.IsSome)
    Assert.Empty refused.Items
    Assert.Empty foreign.Mutations

[<Fact>]
let ``identical inspection snapshots are identical and malformed binding reads nothing`` () =
    let snapshot () = scripted (inspectionResponses (native 2963 "2026-10-02T00:00:00Z") (ok "[]") planningValues)
    let first = V2ProjectionSource.inspectFixed (snapshot ()) canonicalBinding None
    let second = V2ProjectionSource.inspectFixed (snapshot ()) canonicalBinding None
    Assert.Equal(first, second)
    Assert.Equal(DateTimeOffset.MinValue, first.NativeIntegrator.ObservedAt)
    let noCalls = scripted []
    for candidate in [ { canonicalBinding with ProjectNumber = 1 }; { canonicalBinding with ArtifactSha256 = String.replicate 64 "0" } ] do
        let report = V2ProjectionSource.inspectFixed noCalls candidate None
        Assert.True(report.Evidence.PopulationGap.IsSome)
        Assert.Equal(DateTimeOffset.MinValue, report.NativeIntegrator.ObservedAt)
        Assert.Empty report.Items
    Assert.Empty noCalls.Mutations

[<Fact>]
let ``complete inspection verifies observed coverage without creating projection history`` () =
    let forIssue number (response: IoResult<Transport.Response>) =
        response |> Result.map (fun value -> { value with Body = value.Body.Replace("2963", string number).Replace("I_native", if number = 2963 then "I_native" else $"I_{number}") })
    let rows = [2963; 2964; 2965] |> List.collect (fun number ->
        [native number "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "owning roadmap"; organization; target; forIssue number membership
         forIssue number (planningValues |> Result.map (fun response -> { response with Body = response.Body.Replace("Unknown", "Verified").Replace("observation_unknown", "observation_verified") })) ])
    let transport = scripted (protectedBlob canonicalManifest :: rows)
    let report = V2ProjectionSource.inspectFixed transport canonicalBinding None
    Assert.Equal(3, report.Evidence.Verified)
    Assert.All(report.Items, fun item ->
        Assert.Equal("Current", item.SourceCurrentness)
        Assert.True(item.Evidence.DependencyReadComplete)
        Assert.True(item.Planning.IsSome)
        Assert.True(item.Evidence.Projection.IsNone)
        Assert.True(item.Evidence.LastVerified.IsNone)
        Assert.Equal(0, item.Evidence.MutationAttempts))
    Assert.Empty transport.Mutations

[<Fact>]
let ``inspection preserves only historical verification and cannot treat it as current`` () =
    let previousTransport = scripted [ protectedBlob canonicalManifest; native 2963 "2026-10-02T00:00:00Z"; ok "[]"; protectedBlob "owning roadmap"; organization; target; membership; observation (Some "Verified"); Error(Http(403,"second")); Error(Http(403,"third")) ]
    let previous = V2ProjectionSource.runFixed previousTransport canonicalBinding None
    let denied = Error(Http(403,"unknown now"))
    let inspection = scripted [protectedBlob canonicalManifest; denied; denied; denied; denied; denied; denied]
    let report = V2ProjectionSource.inspectFixed inspection canonicalBinding (Some previous)
    Assert.Equal(previous.Items.Head.LastVerified, report.Items.Head.Evidence.LastVerified)
    Assert.True(report.Items.Head.Evidence.LastVerified.IsSome)
    Assert.Equal("Unknown", report.Items.Head.SourceCurrentness)
    Assert.Equal("Unknown", report.Items.Head.Evidence.Health)
    Assert.True(report.Items.Head.Evidence.Projection.IsNone)
    Assert.Empty inspection.Mutations

// Version-3 fixtures use two independently selected receiver projects/repositories.
let private productBinding repository tag number =
    { canonicalBinding with
        BindingVersion = 3; Owner = "ExampleOrg"; OrganizationId = "O_products"
        ProjectNumber = number; ProjectTitle = tag + " V2"; ProjectId = "PVT_" + tag
        Repositories = Set.singleton ("ExampleOrg/" + repository)
        SelectedIssues = Map.ofList [ "I_" + tag, "ExampleOrg/" + repository + "#7" ]
        Status = { binding.Status with Id = tag + "_status" }
        RoadmapFieldId = tag + "_roadmap"
        Track = { binding.Track with Id = tag + "_track" }
        Observation = { Id = tag + "_observation"; Options = Map.ofList [ "Verified", tag + "_verified"; "Stale", tag + "_stale"; "Unknown", tag + "_unknown" ] } }

let private productA = productBinding "WidgetA" "product_a" 81
let private productB = productBinding "WidgetB" "product_b" 82
let private productRequest (selected: Binding) =
    { Issue = { Owner = selected.Owner; Repository = (Set.minElement selected.Repositories).Split('/')[1]; Number = 7 }
      ExpectedNodeId = selected.SelectedIssues |> Map.keys |> Seq.exactlyOne }

let private productResponse (selected: Binding) (response: IoResult<Transport.Response>) =
    response |> Result.map (fun value ->
        let body =
            value.Body.Replace("FS-GG/.github", Set.minElement selected.Repositories).Replace("FS-GG", selected.Owner)
                .Replace("O_fsgg", selected.OrganizationId).Replace("PVT_coord_v2", selected.ProjectId)
                .Replace("Coordination V2", selected.ProjectTitle).Replace("\"number\":77", $"\"number\":{selected.ProjectNumber}")
                .Replace("PVTI_2963", "PVTI_" + selected.ProjectId).Replace("I_native", (productRequest selected).ExpectedNodeId)
                .Replace("2963", "7").Replace("PVTSSF_status_v2", selected.Status.Id).Replace("PVTF_roadmap_v2", selected.RoadmapFieldId)
                .Replace("PVTSSF_track_v2", selected.Track.Id).Replace("PVTSSF_observation_v2", selected.Observation.Id)
                .Replace("observation_verified", selected.Observation.Options["Verified"]).Replace("observation_stale", selected.Observation.Options["Stale"])
                .Replace("observation_unknown", selected.Observation.Options["Unknown"])
        { value with Body = body })

let private productManifest (selected: Binding) =
    Text.Json.JsonSerializer.Serialize
        {| schema = "fsgg.coordination-board-v2-import/v1"
           target = {| ownerKind = "organization"; owner = selected.Owner; title = selected.ProjectTitle; id = selected.ProjectId
                       number = selected.ProjectNumber; creationState = "created-and-read-back" |}
           binding = {| organizationId = selected.OrganizationId; visibility = "complete"; authorization = "root-selected"
                        recipeRevision = selected.ImportRecipeRevision; artifactSha256 = selected.ImportArtifactSha256; repositories = Set.toList selected.Repositories |}
           items = [ {| issue = selected.SelectedIssues |> Map.values |> Seq.exactlyOne; nodeId = (productRequest selected).ExpectedNodeId
                        observedUpdatedAt = "2026-10-02T00:00:00Z"; observedState = "open"; decision = "import"
                        adjudication = "verified-remaining"; pilot = true; roadmap = "docs/product-plan.md"; dependencies = Array.empty<obj> |} ] |}

let private productBlob selected text = productResponse selected (protectedBlob text)
let private productNative selected updated = productResponse selected (native 2963 updated)
let private productSource selected =
    [ productNative selected "2026-10-02T00:00:00Z"; ok "[]"; productBlob selected "owned product plan" ]
let private productProject selected observationValue =
    [ productResponse selected organization; productResponse selected target; productResponse selected membership
      productResponse selected (observation observationValue) ]

[<Fact>]
let ``two receiver products use their own protected population and planning target`` () =
    for selected in [ productA; productB ] do
        let transport = scripted (productBlob selected (productManifest selected) :: (productSource selected @ productProject selected (Some "Verified")))
        let report = V2ProjectionSource.runFixed transport selected None
        Assert.Equal(Some 1, report.Selected)
        Assert.Equal(1, report.Verified)
        Assert.Equal(selected.ProjectId, report.ProjectId)
        Assert.Equal((productRequest selected).Issue, report.Items.Head.Issue)
        Assert.Equal(0, report.Items.Head.MutationAttempts)
        Assert.Empty(transport.Mutations)
        Assert.Contains(Set.minElement selected.Repositories, report.ProtectedInputs.Head |> fun (path, _, _) -> path)
        Assert.True(transport.Logged (Set.minElement selected.Repositories + "#7 native observation"))
        let inspectionReads =
            scripted (
                productBlob selected (productManifest selected)
                :: (productSource selected @ [ Error(Http(403, "planning unavailable")) ]))
        let inspection = V2ProjectionSource.inspectFixed inspectionReads selected None
        Assert.Equal("Current", inspection.Items.Head.SourceCurrentness)
        Assert.True(inspection.Items.Head.Evidence.Native.IsSome)
        Assert.Equal(DateTimeOffset.MinValue, inspection.NativeIntegrator.ObservedAt)
        Assert.Empty inspection.NativeIntegrator.PullRequests
        Assert.Empty inspectionReads.Mutations

[<Fact>]
let ``receiver refresh guards its one write and duplicate retry independently reads`` () =
    for selected in [ productA; productB ] do
        let manifest = productBlob selected (productManifest selected)
        let responses =
            [ manifest ] @ productSource selected @ productProject selected (Some "Stale")
            @ [ manifest ] @ productSource selected @ [ productResponse selected (observation None); productResponse selected (observation (Some "Verified")) ]
        let reads = scripted responses
        let mutable writes = 0
        let guarded = V2ObservationTransport.compose selected reads (fun _ -> writes <- writes + 1; mutationSuccess) |> Result.defaultWith (Errors.explain >> failwith)
        let first = V2ProjectionSource.runFixed guarded selected None
        Assert.Equal(1, first.Verified)
        Assert.Equal(1, first.Items.Head.MutationAttempts)
        Assert.Equal(1, writes)
        let duplicateReads = scripted (manifest :: (productSource selected @ productProject selected (Some "Verified")))
        let duplicate = V2ObservationTransport.compose selected duplicateReads (fun _ -> failwith "duplicate write") |> Result.defaultWith (Errors.explain >> failwith)
        let second = V2ProjectionSource.runFixed duplicate selected (Some first)
        Assert.Equal(1, second.Verified)
        Assert.Equal(0, second.Items.Head.MutationAttempts)
        Assert.Equal(1, writes)

[<Fact>]
let ``receiver population source cannot fall back to organization or other product`` () =
    // The organization cohort has several issues, so use its native blob fixture
    // directly rather than the single-issue product response constructor.
    for response in [ productBlob productB (productManifest productA)
                      protectedBlob (productManifest productA) ] do
        let transport = scripted [ response ]
        let report = V2ProjectionSource.runFixed transport productA None
        Assert.True(report.PopulationGap.IsSome)
        Assert.True(report.Selected.IsNone)
        Assert.Equal(1, transport.GraphQlCalls)
        Assert.Equal(0, report.Attempted)
        Assert.Empty(transport.Mutations)
    for manifest in [ (productManifest productA).Replace("complete", "unknown")
                      (productManifest productA).Replace(productA.ImportArtifactSha256, String.replicate 64 "0")
                      productManifest productB ] do
        let transport = scripted [ productBlob productA manifest ]
        Assert.True((V2ProjectionSource.runFixed transport productA None).PopulationGap.IsSome)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``receiver stale denied incomplete dependency and changed fields emit no mutation`` () =
    let variants =
        [ [ productBlob productA (productManifest productA); productNative productA "2026-10-02T01:00:00Z" ]
          [ productBlob productA (productManifest productA); Error(Http(403, "denied")) ]
          [ productBlob productA (productManifest productA); productNative productA "2026-10-02T00:00:00Z"; ok "[]" |> Result.map (fun response -> { response with NextLink = Some "https://api.github.com/next" }) ]
          [ productBlob productA (productManifest productA) ] @ productSource productA
          @ [ productResponse productA organization; productResponse productA (ok (exactProject binding.ProjectId "changed-observation-field")) ] ]
    for responses in variants do
        let transport = scripted responses
        let report = V2ProjectionSource.runFixed transport productA None
        Assert.Equal(0, report.Verified)
        Assert.Equal(0, report.Items.Head.MutationAttempts)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``receiver unsupported owner organization legacy and cross repository refuse before IO`` () =
    for bad in [ { productA with OwnerKind = OwnerKind.User }
                 { productA with ProjectId = "PVT_kwDOEYAWY84Bb08W" }
                 { productA with ProjectId = "PVT_kwDOEYAWY84Bldpa" }
                 { productA with Owner = "FS-GG"; ProjectNumber = 3; ProjectId = "PVT_kwDOEYAWY84Bldpa" }
                 { productA with Owner = "FS-GG"; ProjectNumber = 1; ProjectId = "PVT_kwDOEYAWY84Bb08W" }
                 { productA with Repositories = Set.union productA.Repositories productB.Repositories }
                 { productA with SelectedIssues = productB.SelectedIssues } ] do
        let transport = scripted []
        Assert.True((V2ProjectionSource.runFixed transport bad None).PopulationGap.IsSome)
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    let transport = scripted []
    let mutable verified = false
    let source: SourceVerifier = fun _ -> verified <- true; Ok(Current "fixture")
    Assert.True(runOneShot source transport productA (productRequest productB) |> Result.isError)
    Assert.False(verified)
    Assert.Equal(0, transport.GraphQlCalls)

[<Fact>]
let ``receiver cohort permits one through five exact local identities only`` () =
    let cohort count =
        [ 1 .. count ] |> List.map (fun number -> "I_product_" + string number, "ExampleOrg/WidgetA#" + string number) |> Map.ofList
    for count in [ 1; 5 ] do
        Assert.True(validateBinding { productA with SelectedIssues = cohort count } |> Result.isOk)
    for selected in [ cohort 0; cohort 6
                      Map.ofList [ "I_one", "ExampleOrg/WidgetA#7"; "I_two", "ExampleOrg/WidgetA#7" ]
                      Map.ofList [ "I_one", "ExampleOrg/WidgetA#07" ] ] do
        Assert.True(validateBinding { productA with SelectedIssues = selected } |> Result.isError)
    let reads = scripted []
    let mutable sourceCalls = 0
    let source: SourceVerifier = fun _ -> sourceCalls <- sourceCalls + 1; Ok(Current "fixture")
    for node in [ "I_foreign"; "" ] do
        let request = { (productRequest productA) with ExpectedNodeId = node }
        Assert.True(runOneShot source reads productA request |> Result.isError)
        Assert.True(readPlanning reads productA request |> Result.isError)
    Assert.Equal(0, sourceCalls)
    Assert.Equal(0, reads.GraphQlCalls)
    Assert.Empty(reads.Mutations)

[<Fact>]
let ``receiver fresh organization and exact project ownership must match the binding`` () =
    for responses, calls in
        [ [ productResponse productB organization |> Result.map (fun response -> { response with Body = response.Body.Replace("O_products", "O_foreign") }) ], 1
          [ productResponse productA organization; productResponse productB target ], 2 ] do
        let reads = scripted responses
        Assert.True(runOneShot (fun _ -> Ok(Current "fixture")) reads productA (productRequest productA) |> Result.isError)
        Assert.Equal(calls, reads.GraphQlCalls)
        Assert.Empty(reads.Mutations)

[<Fact>]
let ``receiver membership pagination must complete before any selected write`` () =
    for selected in [ productA; productB ] do
        let first = productResponse selected membership |> Result.map (fun response ->
            { response with Body = response.Body.Replace("\"hasNextPage\":false,\"endCursor\":null", "\"hasNextPage\":true,\"endCursor\":\"next\"") })
        let transport = scripted [ productResponse selected organization; productResponse selected target; first; Error(Http(403, "membership page denied")) ]
        Assert.True(runOneShot (fun _ -> Ok(Current "fixture")) transport selected (productRequest selected) |> Result.isError)
        Assert.Equal(4, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)

[<Fact>]
let ``receiver lost dispatch response remains unknown and repeat uses independent current read`` () =
    for selected in [ productA; productB ] do
        let manifest = productBlob selected (productManifest selected)
        let responses = [ manifest ] @ productSource selected @ productProject selected (Some "Stale") @ [ manifest ] @ productSource selected @ [ productResponse selected (observation None) ]
        let reads = scripted responses
        let mutable writes = 0
        let guarded = V2ObservationTransport.compose selected reads (fun _ -> writes <- writes + 1; Error(Transport "native response lost")) |> Result.defaultWith (Errors.explain >> failwith)
        let unknown = V2ProjectionSource.runFixed guarded selected None
        Assert.Equal(0, unknown.Verified)
        Assert.True(unknown.Items.Head.Gap.IsSome)
        Assert.Equal(1, writes)
        Assert.True(guarded.RetryMutation "lost" |> Result.isError)
        let repeatReads = scripted (manifest :: (productSource selected @ productProject selected (Some "Verified")))
        let repeat = V2ObservationTransport.compose selected repeatReads (fun _ -> failwith "lost-response retry dispatched again") |> Result.defaultWith (Errors.explain >> failwith)
        let report = V2ProjectionSource.runFixed repeat selected (Some unknown)
        Assert.Equal(1, report.Verified)
        Assert.Equal(0, report.Items.Head.MutationAttempts)
        Assert.Equal(1, writes)

[<Fact>]
let ``receiver population blob drift before dispatch preserves the field`` () =
    for selected in [ productA; productB ] do
        let manifest = productBlob selected (productManifest selected)
        let changed = manifest |> Result.map (fun response -> { response with Body = response.Body.Replace(String.replicate 40 "b", String.replicate 40 "d") })
        let reads = scripted ([ manifest ] @ productSource selected @ productProject selected None @ [ changed ])
        let mutable writes = 0
        let guarded = V2ObservationTransport.compose selected reads (fun _ -> writes <- writes + 1; mutationSuccess) |> Result.defaultWith (Errors.explain >> failwith)
        let report = V2ProjectionSource.runFixed guarded selected None
        Assert.Equal(0, report.Verified)
        Assert.Equal(0, report.Items.Head.MutationAttempts)
        Assert.Equal(0, writes)
        Assert.True(report.Items.Head.Gap.IsSome)

let private programmeSnapshot (change: Transport.Request -> IoResult<Transport.Response> -> IoResult<Transport.Response>) : V2ProjectionSource.InspectionReport =
    let transport = ProgrammeFixture.transport change
    let report = V2ProjectionSource.inspectFixed transport (ProgrammeFixture.binding ()) None
    Assert.Empty transport.Mutations
    report

let private selectionContext (report: V2ProjectionSource.InspectionReport) = ProgrammeFixture.context report

let private originalReservation owner repository paths : V2ProjectionSource.Reservation =
    { SliceId = "original-slice"; Attempt = "original-attempt"; Operation = V2ProjectionSource.Implement
      Owner = owner; Repository = repository; Paths = paths; PullRequest = None; Head = None; CustodyReference = "original-custodian/hold" }

[<Fact>]
let ``programme production composition reads current source and complete native facts without enrollment edits`` () =
    let report = programmeSnapshot ProgrammeFixture.unchanged
    Assert.True(report.Evidence.PopulationGap.IsNone, string report.Evidence.PopulationGap)
    Assert.Equal(Some 1, report.Evidence.Selected)
    Assert.Equal("Current", report.Items.Head.SourceCurrentness)
    Assert.Equal(Some "In review", report.Items.Head.Planning |> Option.bind _.Status)
    Assert.Equal(Some "Unknown", report.Items.Head.Planning |> Option.bind _.Observation)
    Assert.Equal(2, report.Memberships.Length)
    Assert.Single(report.Memberships |> List.filter (fun value -> not value.Enrolled)) |> ignore
    let pull = Assert.Single report.NativeIntegrator.PullRequests
    Assert.Equal(ProgrammeFixture.head, pull.Head)
    Assert.Equal<string list>([ "src/current.fs" ], pull.Files)
    Assert.NotEmpty pull.Reviews
    Assert.NotEmpty pull.Checks
    Assert.Empty pull.Gaps
    let context = selectionContext report
    let selected = V2ProjectionSource.selectActions report (Some { context with ImplementationSlots = None; ReviewSlots = None; HostedCiSlots = None; Reservations = None }) report.NativeIntegrator.ObservedAt
    Assert.Equal("continue-existing", selected.Head.Decision)
    Assert.Equal(Some pull.Url, selected.Head.PullRequest)
    Assert.Equal(Some "worker", selected.Head.Owner)
    Assert.DoesNotContain(selected.Head.Evidence, fun value -> value = "author")
    let unavailable = V2ProjectionSource.selectActions report None report.NativeIntegrator.ObservedAt
    Assert.Equal("unknown", unavailable.Head.Decision)
    Assert.Equal(Some pull.Url, unavailable.Head.PullRequest)

[<Fact>]
let ``folded programme blockers keep every REST blocked_by refusal`` () =
    // #5119: blockers ride on the native observation GraphQL read; the REST reader's checks still refuse.
    let foreign = box {| id = "I_foreign"; number = 7; updatedAt = "2026-10-17T12:00:00Z"; state = "OPEN"; url = "https://github.com/Other/repo/issues/7"; repository = {| nameWithOwner = "Other/repo" |} |}
    let wrongUrl = box {| id = "I_dependency"; number = 1; updatedAt = "2026-10-17T12:00:00Z"; state = "OPEN"; url = "https://github.com/FS-GG/.github/pull/1"; repository = {| nameWithOwner = "FS-GG/.github" |} |}
    let noNode = ProgrammeFixture.blocker 1 "" "2026-10-17T12:00:00Z" "OPEN"
    let lowerState = ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T12:00:00Z" "open"
    let many = [ for number in 1 .. 50 -> ProgrammeFixture.blocker number $"I_{number}" "2026-10-17T12:00:00Z" "OPEN" ]
    let truncated (request: Transport.Request) (response: IoResult<Transport.Response>) =
        ProgrammeFixture.withBlockers [ ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T12:00:00Z" "OPEN" ] request response
        |> Result.map (fun value -> if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then { value with Body = value.Body.Replace("\"totalCount\":1", "\"totalCount\":2") } else value)
    let cases =
        [ "not an FS-GG issue", ProgrammeFixture.withBlockers [ foreign ]
          "not an FS-GG issue", ProgrammeFixture.withBlockers [ wrongUrl ]
          "node identity missing/mismatched", ProgrammeFixture.withBlockers [ noNode ]
          "node identity missing/mismatched", ProgrammeFixture.withBlockers [ lowerState ]
          "bound reached; completeness unknown", ProgrammeFixture.withBlockers many
          "incomplete or denied", truncated ]
    // The final barrier replaces the item gap with its own, so each case is identified by its outcome: a refused
    // blocker snapshot is never a complete dependency read and never Current. Each case alone keeps its own check.
    for expected, change in cases do
        let report = programmeSnapshot change
        let item = report.Items.Head
        Assert.True(("Unknown" = item.SourceCurrentness), expected)
        Assert.False(item.Evidence.DependencyReadComplete, expected)
        Assert.Empty(item.Evidence.DependencyObservations)
    // The accepted shape, and no REST dependency read at all.
    let transport = ProgrammeFixture.transport (ProgrammeFixture.withBlockers [ ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T12:00:00Z" "CLOSED" ])
    let report = V2ProjectionSource.inspectFixed transport (ProgrammeFixture.binding ()) None
    Assert.Equal("Current", report.Items.Head.SourceCurrentness)
    Assert.Equal<string list>([ "I_dependency@2026-10-17T12:00:00Z:CLOSED" ], report.Items.Head.Evidence.DependencyObservations |> List.map (fun value -> $"{value.NodeId}@{value.UpdatedAt}:{value.State}"))
    Assert.Equal(0, transport.Count "dependencies/blocked_by")

[<Fact>]
let ``a Work level outside the bound schema on the planning membership read is a planning gap`` () =
    // #5119: Work level rides on the successor planning membership read with the former classification read's checks.
    let mutable classifications = 0
    let wrongOption (request: Transport.Request) (response: IoResult<Transport.Response>) =
        if request.Subject = "selected work classification" then classifications <- classifications + 1
        if request.Subject <> "bound V2 successor planning memberships" then response
        else response |> Result.map (fun value -> { value with Body = value.Body.Replace("\"delivery-option\"", "\"foreign-option\"") })
    let reads = ProgrammeFixture.successorTransport (ProgrammeFixture.successorBinding ()) (Some "Delivery") wrongOption
    let report = V2ProjectionSource.inspectFixed reads (ProgrammeFixture.successorBinding ()) None
    let gap = report.Items.Head.PlanningGap |> Option.map Errors.explain |> Option.defaultValue ""
    Assert.Contains("Work level field/value differs from bound schema", gap)
    Assert.Equal(0, classifications)
    let unset = ProgrammeFixture.successorTransport (ProgrammeFixture.successorBinding ()) None ProgrammeFixture.unchanged
    let unsetReport = V2ProjectionSource.inspectFixed unset (ProgrammeFixture.successorBinding ()) None
    Assert.Equal(Some("Unknown", None), unsetReport.Items.Head.Planning |> Option.bind _.WorkLevel)

[<Fact>]
let ``ordinary closure dependency and plan revisions are fresh programme observations`` () =
    let change (request: Transport.Request) (response: IoResult<Transport.Response>) =
        let response =
            response |> Result.map (fun result ->
                if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then { result with Body = result.Body.Replace("\"state\":\"OPEN\"", "\"state\":\"CLOSED\"").Replace(ProgrammeFixture.revision, "2026-10-17T13:00:00Z") }
                elif request.Subject.EndsWith("programme.md", StringComparison.Ordinal) then { result with Body = result.Body.Replace(ProgrammeFixture.head, String.replicate 40 "f") }
                else result)
        ProgrammeFixture.withBlockers [ ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T13:00:00Z" "CLOSED" ] request response
    let report = programmeSnapshot change
    Assert.Equal("Current", report.Items.Head.SourceCurrentness)
    Assert.Equal("CLOSED", report.Items.Head.Evidence.Native.Value.State)
    Assert.Single report.Items.Head.Evidence.DependencyObservations |> ignore
    Assert.Equal("needs-decision", (V2ProjectionSource.selectActions report (Some(selectionContext report)) report.NativeIntegrator.ObservedAt).Head.Decision)

[<Fact>]
let ``programme missing partial foreign and moved native facts never produce continuation or ready`` () =
    for subjectPart in [ "open PR inventory"; "closingIssuesReferences"; "files"; "final head barrier"; "complete V2 project memberships" ] do
        let report = programmeSnapshot (fun request response -> if request.Subject.Contains subjectPart then Error(Http(403, "inert denied read")) else response)
        let selected = V2ProjectionSource.selectActions report (Some(selectionContext report)) report.NativeIntegrator.ObservedAt
        Assert.Equal("unknown", selected.Head.Decision)
        if subjectPart = "complete V2 project memberships" then
            Assert.Equal(DateTimeOffset.MinValue, report.NativeIntegrator.ObservedAt)
            let context = selectionContext report
            let missingContext = { context with ObservedAt = DateTimeOffset.MinValue; ValidUntil = DateTimeOffset.MinValue.AddMinutes(1.0) }
            let absentReport = { report with MembershipGap = None; Evidence = { report.Evidence with PopulationGap = None } }
            let absent = (V2ProjectionSource.selectActions absentReport (Some missingContext) DateTimeOffset.MinValue).Head
            Assert.Equal("unknown", absent.Decision)
            Assert.Contains(absent.Reasons, fun value -> value.Contains("Native integrator snapshot expired"))
    let mutations: (string * string * string) list =
        [ (ProgrammeFixture.head, String.replicate 40 "f", "final head barrier");
          ("I_outcome", "I_foreign", "native observation");
          ("FS-GG/.github", "Outside/Foreign", "open PR inventory") ]
    for oldValue, newValue, subject in mutations do
        let report = programmeSnapshot (fun request response ->
            response |> Result.map (fun value -> if request.Subject.Contains subject then { value with Body = value.Body.Replace(oldValue, newValue) } else value))
        Assert.Equal("unknown", (V2ProjectionSource.selectActions report (Some(selectionContext report)) report.NativeIntegrator.ObservedAt).Head.Decision)

[<Fact>]
let ``declared work reconciliation is local while independent slices retain ordered source capacity`` () =
    let other: IssueRef = { Owner = "FS-GG"; Repository = ".github"; Number = 5555 }
    let rows: (string * IssueRef * string) list =
        [ ("I_outcome", ProgrammeFixture.issue, "docs/coordination/programme.md");
          ("I_other_outcome", other, "docs/coordination/programme.md") ]
    let selected: Binding =
        { ProgrammeFixture.binding () with SelectedIssues = Map.ofList [ ("I_outcome", "FS-GG/.github#4634"); ("I_other_outcome", "FS-GG/.github#5555") ] }
        |> fun binding -> ProgrammeFixture.enroll binding rows
    let emptyBody = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "None")
    let body = ProgrammeFixture.ownerBody.Replace("https://github.com/FS-GG/.github/pull/42", "Retain original receiver custody while changing independent source")
    let change (request: Transport.Request) (response: Errors.IoResult<Transport.Response>) =
        if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
            response |> Result.map (fun (value: Transport.Response) ->
                { value with Body = value.Body.Replace(System.Text.Json.JsonSerializer.Serialize emptyBody, System.Text.Json.JsonSerializer.Serialize body) })
        else response
    let transport = ProgrammeFixture.populationTransport selected rows change
    let report = V2ProjectionSource.inspectFixed transport selected None
    Assert.True(report.Evidence.PopulationGap.IsNone)
    Assert.All<V2ProjectionSource.InspectionItem>(report.Items, fun item -> Assert.Equal<string option>(Some "Retain original receiver custody while changing independent source", item.CurrentDeliverable))
    let context = selectionContext report
    let template = context.Slices.Head
    let makeSlice (item: V2ProjectionSource.InspectionItem) (id: string) (satisfied: bool option) : V2ProjectionSource.CurrentSlice =
        let native = item.Evidence.Native.Value
        { template with
            Id = id; Attempt = id + "-attempt"; Issue = item.Evidence.Issue; Operation = V2ProjectionSource.Implement;
            Paths = [ "src/" + id + ".fs" ]; PullRequest = None; PullRequestId = None; PullRequestUpdatedAt = None; Head = None;
            Join = { NativeId = native.NodeId; UpdatedAt = native.UpdatedAt; BodySha256 = native.BodySha256; SourceRevision = item.SourceRevision.Value; HierarchySha256 = None };
            Prerequisites = [ { Action = V2ProjectionSource.Source; Reference = "current-deliverable"; Satisfied = satisfied; SupplyingOwner = "programme-owner" } ] }
    let unknownItem = report.Items |> List.find (fun item -> item.Evidence.Issue = ProgrammeFixture.issue)
    let independentItem = report.Items |> List.find (fun item -> item.Evidence.Issue = other)
    let proposed =
        { context with ImplementationSlots = Some 2;
                       Slices = [ makeSlice unknownItem "unknown-work" None;
                                  makeSlice independentItem "source-one" (Some true);
                                  makeSlice independentItem "source-two" (Some true) ] }
    let decisions = V2ProjectionSource.selectActions report (Some proposed) report.NativeIntegrator.ObservedAt
    Assert.Equal<string list>([ "unknown"; "ready"; "ready" ], decisions |> List.map _.Decision)
    Assert.Equal<string list>([ "unknown-work"; "source-one"; "source-two" ], decisions |> List.map (fun value -> value.SliceId.Value))
    Assert.All<V2ProjectionSource.Selection>(decisions, fun value -> Assert.True(value.PullRequest.IsNone))
    Assert.All<V2ProjectionSource.InspectionItem>(report.Items, fun item -> Assert.Equal<string option>(Some "Blocked", item.Planning.Value.Status))
    Assert.Empty transport.Mutations

[<Fact>]
let ``programme selection uses actual slices nonenrolled work and action specific prerequisites`` () =
    let report = programmeSnapshot ProgrammeFixture.unchanged
    let current = report.NativeIntegrator.PullRequests.Head
    let report = { report with Items = [ { report.Items.Head with Deliverables = [] } ]; NativeIntegrator = { report.NativeIntegrator with PullRequests = [ { current with Issues = [ "FS-GG/.github#999" ] } ] } }
    let context = selectionContext report
    let choose (context: V2ProjectionSource.SelectionContext) (report: V2ProjectionSource.InspectionReport) = (V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt).Head
    Assert.Equal("ready", (choose context report).Decision)
    let conflict = { context with Slices = [ { context.Slices.Head with Paths = [ "src/current.fs" ] } ] }
    Assert.Equal("blocked", (choose conflict report).Decision)
    Assert.Equal("unknown", (choose { context with Reservations = None } report).Decision)
    Assert.Equal("ready", (choose { context with ReviewSlots = Some 0 } report).Decision)
    Assert.Equal("unknown", (choose { context with Slices = [ { context.Slices.Head with PrerequisitesComplete = false } ] } report).Decision)
    let later: V2ProjectionSource.Prerequisite = { Action = V2ProjectionSource.Activation; Reference = "publication still required"; Satisfied = Some false; SupplyingOwner = "publisher" }
    let source = { context with Slices = [ { context.Slices.Head with Prerequisites = later :: context.Slices.Head.Prerequisites } ] }
    Assert.Equal("ready", (choose source report).Decision)
    let activate = { source with Slices = [ { source.Slices.Head with Action = V2ProjectionSource.Activation; Prerequisites = source.Slices.Head.Prerequisites |> List.map (fun prerequisite -> if prerequisite.Reference = "current-deliverable" then { prerequisite with Action = V2ProjectionSource.Activation } else prerequisite) } ] }
    Assert.Equal("blocked", (choose activate report).Decision)
    let duplicate = { context with Slices = context.Slices @ context.Slices }
    Assert.Equal("needs-decision", (choose duplicate report).Decision)
    let reservation: V2ProjectionSource.Reservation = originalReservation "unknown-original" "FS-GG/.github" [ "src/new.fs" ]
    Assert.Equal("blocked", (choose { context with Reservations = Some [ reservation ] } report).Decision)
    Assert.Equal("unknown", (choose { context with ValidUntil = context.ObservedAt.AddSeconds(-1.0) } report).Decision)
    let ambiguous = { report with NativeIntegrator = { report.NativeIntegrator with PullRequests = [ { current with Issues = [ "FS-GG/.github#4634" ] }; { current with Id = "second"; Url = current.Url + "0"; Issues = [ "FS-GG/.github#4634" ] } ] } }
    Assert.Equal("needs-decision", (choose context ambiguous).Decision)

[<Fact>]
let ``programme writer requires successor schema and preserves product schema`` () =
    let binding = ProgrammeFixture.binding ()
    let transport = scripted []
    Assert.True(V2ObservationTransport.validateScope binding |> Result.isOk)
    let invalid = { binding with SchemaVersion = 1 }
    let result = runOneShot (fun _ -> failwith "invalid schema must not invoke verifier") transport invalid { Issue = ProgrammeFixture.issue; ExpectedNodeId = "I_outcome" }
    Assert.True(Result.isError result)
    Assert.True((V2ProjectionSource.runFixed transport invalid None).PopulationGap.IsSome)
    Assert.Equal(0, transport.GraphQlCalls)
    Assert.True(validateBinding { binding with SchemaVersion = 1 } |> Result.isError)
    Assert.True(validateBinding { binding with ProjectId = "PVT_foreign" } |> Result.isError)
    Assert.True(validateBinding { productA with Status = binding.Status } |> Result.isError)


[<Fact>]
let ``programme concurrent human planning membership and source edits invalidate selection`` () =
    let mutations: (string * string * string) list =
        [ ("In review", "Ready", "bound V2 planning fields");
          ("docs/coordination/programme.md", "docs/other-plan.md", "bound V2 planning fields");
          ("I_extra", "I_new_extra", "complete V2 project memberships");
          (ProgrammeFixture.revision, "2026-10-17T14:00:00Z", "native observation") ]
    for oldValue, newValue, subject in mutations do
        let mutable matches = 0
        let report = programmeSnapshot (fun request response ->
            if request.Subject.Contains subject then
                matches <- matches + 1
                if matches >= 2 then response |> Result.map (fun value -> { value with Body = value.Body.Replace(oldValue, newValue) }) else response
            else response)
        Assert.Equal("unknown", (V2ProjectionSource.selectActions report (Some(selectionContext report)) report.NativeIntegrator.ObservedAt).Head.Decision)


[<Fact>]
let ``actual six heading owner record preserves owner and refuses missing duplicate or malformed fields`` () =
    let bodyChange (body: string) (request: Transport.Request) (response: IoResult<Transport.Response>) =
        if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
            response |> Result.map (fun value -> { value with Body = value.Body.Replace(Text.Json.JsonSerializer.Serialize ProgrammeFixture.ownerBody, Text.Json.JsonSerializer.Serialize(body: string)) })
        else response
    let owner = "Programme owner /root/programme\nMaintainer supplies continuation"
    let report = programmeSnapshot (bodyChange (ProgrammeFixture.ownerBody.Replace("\nworker\n", "\n" + owner + "\n")))
    Assert.Equal(Some owner, report.Items.Head.Owner)
    let bodies: string list =
        [ ProgrammeFixture.ownerBody.Replace("## Owner", "## Missing owner");
          ProgrammeFixture.ownerBody + "\n\n## Owner\n\nsecond owner";
          ProgrammeFixture.ownerBody.Replace("## Owner", "##Owner");
          ProgrammeFixture.ownerBody.Replace("\nworker\n", "\n\n") ]
    for body in bodies do
        let report = programmeSnapshot (bodyChange body)
        Assert.True(report.Items.Head.Owner.IsNone)
        Assert.Equal("needs-decision", (V2ProjectionSource.selectActions report None report.NativeIntegrator.ObservedAt).Head.Decision)

let private asNewAssignment (change: Transport.Request -> IoResult<Transport.Response> -> IoResult<Transport.Response>) : V2ProjectionSource.InspectionReport =
    programmeSnapshot (fun request response ->
        let response = change request response
        if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
            response |> Result.map (fun value -> { value with Body = value.Body.Replace("https://github.com/FS-GG/.github/pull/42", "None") })
        elif request.Subject.EndsWith("closingIssuesReferences", StringComparison.Ordinal) then
            response |> Result.map (fun value -> { value with Body = value.Body.Replace("I_outcome", "I_other").Replace("\"number\":4634", "\"number\":999") })
        else response)

[<Fact>]
let ``actual open native dependency is classified per action without suppressing missing coverage`` () =
    let report = asNewAssignment (ProgrammeFixture.withBlockers [ ProgrammeFixture.blocker 1 "I_dependency" "2026-10-17T12:00:00Z" "OPEN" ])
    Assert.Single report.Items.Head.Evidence.DependencyObservations |> ignore
    let context = selectionContext report
    let choose (context: V2ProjectionSource.SelectionContext) = (V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt).Head
    Assert.Equal("unknown", (choose context).Decision)
    for reference in [ "https://github.com/FS-GG/.github/issues/1"; "FS-GG/.github#1" ] do
        let prerequisite: V2ProjectionSource.Prerequisite = { Action = V2ProjectionSource.Activation; Reference = reference; Satisfied = Some false; SupplyingOwner = "publisher" }
        let classified = { context with Slices = [ { context.Slices.Head with Prerequisites = prerequisite :: context.Slices.Head.Prerequisites } ] }
        Assert.Equal("ready", (choose classified).Decision)
        let activation = { classified with Slices = [ { classified.Slices.Head with Action = V2ProjectionSource.Activation; Prerequisites = classified.Slices.Head.Prerequisites |> List.map (fun prerequisite -> if prerequisite.Reference = "current-deliverable" then { prerequisite with Action = V2ProjectionSource.Activation } else prerequisite) } ] }
        Assert.Equal("blocked", (choose activation).Decision)
        let source = { classified with Slices = [ { classified.Slices.Head with Prerequisites = { prerequisite with Action = V2ProjectionSource.Source } :: context.Slices.Head.Prerequisites } ] }
        Assert.Equal("blocked", (choose source).Decision)
        let mismatched = { classified with Slices = [ { classified.Slices.Head with Prerequisites = { prerequisite with Reference = reference + "0" } :: context.Slices.Head.Prerequisites } ] }
        Assert.Equal("unknown", (choose mismatched).Decision)
        Assert.Equal("unknown", (choose { classified with Slices = [ { classified.Slices.Head with PrerequisitesComplete = false } ] }).Decision)

[<Fact>]
let ``malformed reservation and peer repository or token scopes cannot establish disjointness`` () =
    let report = asNewAssignment ProgrammeFixture.unchanged
    let context = selectionContext report
    let choose (context: V2ProjectionSource.SelectionContext) = (V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt).Head
    Assert.Equal("ready", (choose context).Decision)
    for repository in [ "FS-GG/"; "/"; "/.github"; " FS-GG/.github"; "FS-GG/.github " ] do
        let reservation: V2ProjectionSource.Reservation = originalReservation "original" repository [ "src/unrelated.fs" ]
        Assert.Equal("unknown", (choose { context with Reservations = Some [ reservation ] }).Decision)
        let peer = { context.Slices.Head with Id = "malformed-peer"; Issue = { ProgrammeFixture.issue with Number = 999 }; Repository = repository }
        Assert.Equal("unknown", (choose { context with Slices = context.Slices @ [ peer ] }).Decision)
    for path in [ "any"; "none"; "bad["; "src/a,b.fs"; "src/a`b.fs"; "src/white space.fs" ] do
        let reservation: V2ProjectionSource.Reservation = originalReservation "original" "FS-GG/.github" [ path ]
        Assert.Equal("unknown", (choose { context with Reservations = Some [ reservation ] }).Decision)
        let peer = { context.Slices.Head with Id = "malformed-peer"; Issue = { ProgrammeFixture.issue with Number = 999 }; Paths = [ path ] }
        Assert.Equal("unknown", (choose { context with Slices = context.Slices @ [ peer ] }).Decision)

[<Fact>]
let ``known intake refusal blocks both new source and established continuation`` () =
    for report in [ programmeSnapshot ProgrammeFixture.unchanged; asNewAssignment ProgrammeFixture.unchanged ] do
        let context = selectionContext report
        let refuse = { context with Slices = [ { context.Slices.Head with IntakeAuthorized = Some false } ] }
        Assert.Equal("blocked", (V2ProjectionSource.selectActions report (Some refuse) report.NativeIntegrator.ObservedAt).Head.Decision)
        let unknown = { context with Slices = [ { context.Slices.Head with IntakeAuthorized = None } ] }
        Assert.Equal("unknown", (V2ProjectionSource.selectActions report (Some unknown) report.NativeIntegrator.ObservedAt).Head.Decision)

[<Fact>]
let ``literal native PR files never become chore sentinel or rewritten touch set tokens`` () =
    for path in [ "any"; "none"; "src/a,b.fs"; "src/a`b.fs"; "src/white space.fs" ] do
        let change (request: Transport.Request) (response: IoResult<Transport.Response>) =
            if request.Subject.EndsWith(" files", StringComparison.Ordinal) then
                response |> Result.map (fun value -> { value with Body = value.Body.Replace("src/current.fs", path) })
            else response
        for report in [ programmeSnapshot change; asNewAssignment change ] do
            Assert.Equal<string list>([ path ], report.NativeIntegrator.PullRequests.Head.Files)
            let context = selectionContext report
            let context = { context with Slices = [ { context.Slices.Head with Paths = [ "any/**" ] } ] }
            Assert.Equal("unknown", (V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt).Head.Decision)

[<Fact>]
let ``native snapshot freshness starts before its first inventory read and cannot renew at completion`` () =
    let mutable firstInventoryRead = None
    let report = programmeSnapshot (fun request response ->
        if request.Subject.EndsWith("open PR inventory", StringComparison.Ordinal) && firstInventoryRead.IsNone then firstInventoryRead <- Some DateTimeOffset.UtcNow
        response)
    Assert.True(firstInventoryRead.IsSome)
    Assert.NotEqual(DateTimeOffset.MinValue, report.NativeIntegrator.ObservedAt)
    Assert.True(report.NativeIntegrator.ObservedAt <= firstInventoryRead.Value)
    let context = selectionContext report
    let expired = report.NativeIntegrator.ObservedAt.AddMinutes(5.0).AddTicks(1L)
    let result = (V2ProjectionSource.selectActions report (Some context) expired).Head
    Assert.Equal("unknown", result.Decision)
    Assert.Contains(result.Reasons, fun value -> value.Contains("Native integrator snapshot expired"))
    let collectedEmpty =
        programmeSnapshot (fun request response ->
            if request.Subject.EndsWith("open PR inventory", StringComparison.Ordinal) then
                ok """{"data":{"repository":{"nameWithOwner":"FS-GG/.github","pullRequests":{"totalCount":0,"nodes":[],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}"""
            elif request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
                response |> Result.map (fun value -> { value with Body = value.Body.Replace("https://github.com/FS-GG/.github/pull/42", "None") })
            else response)
    Assert.Empty collectedEmpty.NativeIntegrator.PullRequests
    Assert.Empty collectedEmpty.NativeIntegrator.RepositoryGaps
    Assert.NotEqual(DateTimeOffset.MinValue, collectedEmpty.NativeIntegrator.ObservedAt)
    let available = V2ProjectionSource.selectActions collectedEmpty (Some(selectionContext collectedEmpty)) collectedEmpty.NativeIntegrator.ObservedAt
    Assert.Equal("ready", available.Head.Decision)

[<Fact>]
let ``programme Observation writer rejoins fresh source for writes and Verified noops independently of eligibility`` () =
    let binding = ProgrammeFixture.binding ()
    let mutable observation = "Unknown"
    let reads = ProgrammeFixture.transport (fun request response -> ProgrammeFixture.observation observation request response)
    let mutable writes = 0
    let native (request: Transport.Request) =
        writes <- writes + 1
        match request.Body with
        | Query(_, variables) ->
            Assert.Equal(Some(VId binding.Observation.Id), variables |> List.tryFind (fst >> (=) "fieldId") |> Option.map snd)
            Assert.Equal(Some(VString binding.Observation.Options["Verified"]), variables |> List.tryFind (fst >> (=) "optionId") |> Option.map snd)
        | _ -> failwith "only typed Observation update may dispatch"
        observation <- "Verified"
        ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
    let guarded = V2ObservationTransport.compose binding reads native |> Result.defaultWith (Errors.explain >> failwith)
    let first = V2ProjectionSource.runFixed guarded binding None
    Assert.Equal(1, first.Verified)
    Assert.Equal(1, writes)
    Assert.Equal(1, first.Items.Head.MutationAttempts)
    Assert.Equal(2, first.Items.Head.Projection.Value.SourceChecks)
    Assert.True(reads.Logged "fresh V2 Observation dispatch identity")
    let second = V2ProjectionSource.runFixed guarded binding (Some first)
    Assert.Equal(1, second.Verified)
    Assert.Equal(0, second.Items.Head.MutationAttempts)
    Assert.Equal(2, second.Items.Head.Projection.Value.SourceChecks)
    Assert.Equal(1, writes)
    Assert.StartsWith("Unknown", second.Items.Head.NativeAcceptance)
    Assert.False(reads.Logged "FS-GG/.github open PR inventory")
    Assert.False(reads.Logged "FS-GG/.github repository policy")
    Assert.True(guarded.RetryMutation "lost" |> Result.isError)

[<Fact>]
let ``programme writer refuses old native schema changed Verified noop source and denied readback without retry`` () =
    let binding = ProgrammeFixture.binding ()
    let mutable writes = 0
    let oldReads = ProgrammeFixture.transport ProgrammeFixture.oldSchema
    let old = V2ObservationTransport.compose binding oldReads (fun _ -> writes <- writes + 1; ok "{}") |> Result.defaultWith (Errors.explain >> failwith)
    Assert.Equal(0, (V2ProjectionSource.runFixed old binding None).Verified)
    Assert.Equal(0, writes)
    let mutable sourceReads = 0
    let changedReads =
        ProgrammeFixture.transport (fun request response ->
            let response = ProgrammeFixture.observation "Verified" request response
            if request.Subject.EndsWith("native observation", StringComparison.Ordinal) then
                sourceReads <- sourceReads + 1
                if sourceReads >= 2 then response |> Result.map (fun value -> { value with Body = value.Body.Replace("Accepted outcome", "Changed outcome") }) else response
            else response)
    let changed = V2ObservationTransport.compose binding changedReads (fun _ -> writes <- writes + 1; ok "{}") |> Result.defaultWith (Errors.explain >> failwith)
    let refused = V2ProjectionSource.runFixed changed binding None
    Assert.Equal(0, refused.Verified)
    Assert.Equal(0, refused.Items.Head.MutationAttempts)
    Assert.Equal(0, writes)
    let mutable observation = "Unknown"
    let readbackReads =
        ProgrammeFixture.transport (fun request response ->
            if observation = "Verified" && request.Subject = "board item PVTI_current Observation" then Error(Transport "readback unavailable")
            else ProgrammeFixture.observation observation request response)
    let readback =
        V2ObservationTransport.compose binding readbackReads (fun _ ->
            writes <- writes + 1
            observation <- "Verified"
            ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}""")
        |> Result.defaultWith (Errors.explain >> failwith)
    let unknown = V2ProjectionSource.runFixed readback binding None
    Assert.Equal(0, unknown.Verified)
    Assert.True(unknown.Items.Head.Gap.IsSome)
    Assert.Equal(1, writes)
    Assert.True(readback.RetryMutation "uncertain" |> Result.isError)
    Assert.Equal(1, writes)

[<Fact>]
let ``programme lost Observation response is independently reconciled with no repeated dispatch`` () =
    let binding = ProgrammeFixture.binding ()
    let mutable observation = "Unknown"
    let reads = ProgrammeFixture.transport (fun request response -> ProgrammeFixture.observation observation request response)
    let mutable writes = 0
    let guarded =
        V2ObservationTransport.compose binding reads (fun _ ->
            writes <- writes + 1
            observation <- "Verified"
            Error(Transport "native response lost; original effect unknown"))
        |> Result.defaultWith (Errors.explain >> failwith)
    let unknown = V2ProjectionSource.runFixed guarded binding None
    Assert.Equal(0, unknown.Verified)
    Assert.Equal(1, writes)
    let recovered = V2ProjectionSource.runFixed guarded binding (Some unknown)
    Assert.Equal(1, recovered.Verified)
    Assert.Equal(0, recovered.Items.Head.MutationAttempts)
    Assert.Equal(1, writes)

[<Fact>]
let ``unclassified or inconsistent native gaps never become source permission`` () =
    let report = programmeSnapshot ProgrammeFixture.unchanged
    let context = selectionContext report
    let original = report.NativeIntegrator.PullRequests.Head
    let cases: V2ProjectionSource.PullRequestFacts list =
        [ { original with Gaps = [ "unclassified evidence" ]; GapDetails = [] };
          { original with Gaps = [ "unclassified evidence" ]; GapDetails = [ { Purpose = V2ProjectionSource.NativeReadPurpose.Unclassified; Detail = "unclassified evidence" } ] };
          { original with Gaps = []; GapDetails = [ { Purpose = V2ProjectionSource.NativeReadPurpose.CheckAvailability; Detail = "hidden check evidence" } ] } ]
    for pull in cases do
        let current = { report with NativeIntegrator = { report.NativeIntegrator with PullRequests = [ pull ] } }
        let selected = V2ProjectionSource.selectActions current (Some context) report.NativeIntegrator.ObservedAt
        Assert.Equal<string>("unknown", selected.Head.Decision)

[<Fact>]
let ``programme commitment fixed vector binds original bytes nonce domain and length`` () =
    let nonce = [| 0uy .. 31uy |] |> Convert.ToHexStringLower
    let enrollment: ProgrammeEnrollment = { NonceHex = nonce; PayloadBase64 = "e30=" }
    let actual = programmeCommitment enrollment |> Result.defaultWith (Errors.explain >> failwith)
    Assert.Equal<string>("b806098f0b36b0d63e71511db8af136980af3b8e17fa6da4c8fa86f614688dab", actual)
    let differentBytes = { enrollment with PayloadBase64 = Convert.ToBase64String(Text.Encoding.UTF8.GetBytes "{ }") }
    Assert.NotEqual<string>(actual, programmeCommitment differentBytes |> Result.defaultWith (Errors.explain >> failwith))
    let differentNonce = { enrollment with NonceHex = String.replicate 64 "1" }
    Assert.NotEqual<string>(actual, programmeCommitment differentNonce |> Result.defaultWith (Errors.explain >> failwith))
    let wrongDomain = Text.Encoding.ASCII.GetBytes "fsgg.coordination-board-v2-enrollment/v3\\0"
    let length = Array.zeroCreate<byte> 8
    Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(length.AsSpan(), 2UL)
    let wrong = Array.concat [ wrongDomain; [| 0uy .. 31uy |]; length; Text.Encoding.UTF8.GetBytes "{}" ] |> Security.Cryptography.SHA256.HashData |> Convert.ToHexStringLower
    Assert.NotEqual<string>(actual, wrong)

[<Fact>]
let ``programme private byte decoder refuses ambiguous excessive or malformed encodings without echo`` () =
    let sensitive = "synthetic-sensitive-never-public"
    let encode (text: string) = Text.Encoding.UTF8.GetBytes text |> Convert.ToBase64String
    let cases: ProgrammeEnrollment list =
        [ { NonceHex = ""; PayloadBase64 = "e30=" };
          { NonceHex = String.replicate 64 "A"; PayloadBase64 = "e30=" };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = "e30" };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = "e30=\n" };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = "/w==" };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = "77u/e30=" };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = encode ("{\"" + sensitive + "\":1,\"" + sensitive + "\":2}") };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = encode (String.replicate 17 "[" + "0" + String.replicate 17 "]") };
          { NonceHex = String.replicate 64 "0"; PayloadBase64 = encode ("{}" + String.replicate 131071 " ") } ]
    for value in cases do
        match programmeCommitment value with
        | Ok _ -> failwith "ambiguous/private byte shape was accepted"
        | Error error -> Assert.DoesNotContain(sensitive, Errors.explain error)

[<Fact>]
let ``programme private enrollment requires native protected commitment and exact closed cohort`` () =
    let selected = ProgrammeFixture.binding ()
    let publicJson = ProgrammeFixture.publicEnrollment selected
    let original = selected.ProgrammeEnrollment |> Option.get
    let payload = Convert.FromBase64String original.PayloadBase64 |> Text.Encoding.UTF8.GetString
    let rows = authenticateProgrammeEnrollment selected publicJson |> Result.defaultWith (Errors.explain >> failwith)
    Assert.Single rows |> ignore
    let changes: string list = [ "target"; "constructor"; "repository"; "issue"; "node"; "roadmap"; "missing"; "duplicate"; "extra" ]
    for change in changes do
        let root = Text.Json.Nodes.JsonNode.Parse payload
        match change with
        | "target" -> root.["target"].["projectId"] <- Text.Json.Nodes.JsonValue.Create "synthetic-other-project"
        | "constructor" -> root.["binding"].["artifactSha256"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 64 "a")
        | "repository" -> root.["binding"].["repositories"].[0] <- Text.Json.Nodes.JsonValue.Create "FS-GG/SyntheticOther"
        | "issue" -> root.["items"].[0].["issue"] <- Text.Json.Nodes.JsonValue.Create "FS-GG/.github#4635"
        | "node" -> root.["items"].[0].["nodeId"] <- Text.Json.Nodes.JsonValue.Create "I_synthetic_other"
        | "roadmap" -> root.["items"].[0].["roadmap"] <- Text.Json.Nodes.JsonValue.Create "docs/other.md"
        | "missing" -> root.["items"].AsArray().Clear()
        | "duplicate" -> root.["items"].AsArray().Add(root.["items"].[0].DeepClone())
        | _ -> root.AsObject().Add("unapproved", Text.Json.Nodes.JsonValue.Create "synthetic-sensitive-never-public")
        let changed = { selected with ProgrammeEnrollment = Some { original with PayloadBase64 = root.ToJsonString() |> Text.Encoding.UTF8.GetBytes |> Convert.ToBase64String } }
        Assert.True(Result.isError (authenticateProgrammeEnrollment changed publicJson), change)
    let invalidPublic: string list =
        [ "{}";
          "{\"schema\":\"fsgg.coordination-board-v2-enrollment/v2\",\"target\":{},\"binding\":{},\"items\":[]}";
          publicJson.Replace("commitmentSha256", "unapproved");
          publicJson.Replace("\"schema\":", "\"schema\":\"duplicate\",\"schema\":");
          publicJson.Replace("}", ",\"private\":\"synthetic-sensitive-never-public\"}") ]
    for invalid in invalidPublic do
        Assert.True(Result.isError (authenticateProgrammeEnrollment selected invalid))
    Assert.DoesNotContain(original.NonceHex, publicJson)
    Assert.DoesNotContain(original.PayloadBase64, publicJson)
    Assert.DoesNotContain("I_outcome", publicJson)

[<Fact>]
let ``programme preparation validates before RNG and final binding retains private bytes exactly`` () =
    let selected = ProgrammeFixture.binding ()
    let payload = selected.ProgrammeEnrollment |> Option.get |> _.PayloadBase64 |> Convert.FromBase64String
    let template = Text.Json.Nodes.JsonNode.Parse(ProgrammeFixture.bindingJsonFor selected)
    template.AsObject().Remove "programmeEnrollment" |> ignore
    let mutable calls = 0
    let rng () = calls <- calls + 1; [| 0uy .. 31uy |]
    Assert.True(Result.isError (prepareProgrammeBinding (template.ToJsonString()) [| 255uy |] rng))
    Assert.Equal(0, calls)
    let oversizedTemplate = Text.Json.Nodes.JsonNode.Parse(template.ToJsonString())
    oversizedTemplate.["roadmapFieldId"] <- Text.Json.Nodes.JsonValue.Create(String.replicate 102400 "r")
    let maximumPayload = Array.append payload (Array.create (131072 - payload.Length) 32uy)
    Assert.True(Result.isError (prepareProgrammeBinding (oversizedTemplate.ToJsonString()) maximumPayload rng))
    Assert.Equal(0, calls)
    let privateJson, publicJson = prepareProgrammeBinding (template.ToJsonString()) payload rng |> Result.defaultWith (Errors.explain >> failwith)
    Assert.Equal(1, calls)
    let actual = decodeBinding privateJson |> Result.defaultWith failwith
    Assert.Equal<byte>(payload, actual.ProgrammeEnrollment |> Option.get |> _.PayloadBase64 |> Convert.FromBase64String)
    Assert.True(Result.isOk (authenticateProgrammeEnrollment actual publicJson))
    Assert.True(Result.isError (decodeBinding (template.ToJsonString())))

[<Fact>]
let ``programme instruction provenance admits both exact observation paths and refuses sibling paths`` () =
    // Exercise the production native/enrollment/plan join with inert transport, not a string-only grammar test.
    // This successor assertion does not qualify any already installed earlier executable.
    let prefix = "https://github.com/FS-GG/.github/blob/" + ProgrammeFixture.head + "/"
    for path, admitted in
        [ ".agents/skills/work-programme/references/observation.md", true
          ".agents/skills/work-board/references/observation.md", true
          ".agents/skills/work-board/SKILL.md", false
          ".agents/skills/work-board/references/other.md", false
          ".claude/skills/work-board/references/observation.md", false ] do
        let rows = [ "I_outcome", ProgrammeFixture.issue, prefix + path ]
        let selected = ProgrammeFixture.enroll (ProgrammeFixture.binding ()) rows
        let reads = ProgrammeFixture.populationTransport selected rows ProgrammeFixture.unchanged
        let report = V2ProjectionSource.inspectFixed reads selected None
        Assert.Empty reads.Mutations
        Assert.True(report.Evidence.PopulationGap.IsNone, string report.Evidence.PopulationGap)
        Assert.Equal(admitted, report.Items.Head.SourceCurrentness = "Current")
        if not admitted then
            Assert.True(report.Items.Head.Evidence.Gap.IsSome)
            Assert.Equal("Unknown", report.Items.Head.Evidence.Health)

[<Fact>]
let ``successor Work level binding is explicit and legacy contracts reject its field`` () =
    let selected = ProgrammeFixture.successorBinding ()
    Assert.True(Result.isOk (validateBinding selected))
    Assert.True(Result.isOk (decodeBinding (ProgrammeFixture.bindingJsonFor selected)))
    for invalid in
        [ { selected with WorkLevel = None }
          { selected with BindingVersion = 4; SchemaVersion = 2 }
          { selected with WorkLevel = Some { selected.WorkLevel.Value with Id = selected.Status.Id } }
          { selected with WorkLevel = Some { selected.WorkLevel.Value with Options = Map.ofList ["Delivery", "delivery-option"] } } ] do
        Assert.True(Result.isError(validateBinding invalid))
    let historical = ProgrammeFixture.binding ()
    Assert.True(Result.isOk(decodeBinding (ProgrammeFixture.bindingJsonFor historical)))

[<Fact>]
let ``explicit delivery has independent capacity while outcome and unknown never select`` () =
    let selected = ProgrammeFixture.successorBinding ()
    for level, decision in [Some "Delivery", "ready"; Some "Outcome", "needs-decision"; None, "unknown"] do
        let reads = ProgrammeFixture.successorTransport selected level ProgrammeFixture.unchanged
        let report = V2ProjectionSource.inspectFixed reads selected None
        let item = report.Items.Head
        Assert.True(item.WorkItem.Value.Complete, string item.WorkItem.Value.Gap)
        Assert.Single(item.WorkItem.Value.Children) |> ignore
        Assert.False(selected.SelectedIssues.ContainsKey item.WorkItem.Value.Children.Head.NodeId)
        let context = ProgrammeFixture.context report
        let result = V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt
        Assert.Equal(decision, result.Head.Decision)
        if level = Some "Delivery" then
            let supplied = context.Slices.Head
            Assert.Equal(Some V2ProjectionSource.Implement, result.Head.Operation)
            Assert.Equal(V2ProjectionSource.Source, result.Head.Action)
            Assert.Equal(supplied.Issue, result.Head.Issue)
            Assert.Equal(Some supplied.Owner, result.Head.Owner)
            Assert.Equal(Some supplied.Id, result.Head.SliceId)
            Assert.Equal(Some supplied.Attempt, result.Head.Attempt)
            Assert.Equal(item.WorkItem.Value.Sha256, supplied.Join.HierarchySha256)
            Assert.Contains(result.Head.Reasons, fun reason -> reason.Contains("action-specific remaining capacity"))
            let exhausted = {context with ImplementationSlots = Some 0}
            Assert.Equal("deferred", (V2ProjectionSource.selectActions report (Some exhausted) report.NativeIntegrator.ObservedAt).Head.Decision)
            let missing = {context with ImplementationSlots = None}
            Assert.Equal("unknown", (V2ProjectionSource.selectActions report (Some missing) report.NativeIntegrator.ObservedAt).Head.Decision)
        Assert.Empty reads.Mutations
        Assert.Contains("unverified", item.WorkItem.Value.ClosureGaps.Head)
        let unselected = { context.Slices.Head with Issue = { ProgrammeFixture.issue with Number = 9999 } }
        let refused = V2ProjectionSource.selectActions report (Some {context with Slices = [unselected]}) report.NativeIntegrator.ObservedAt
        Assert.Equal("unknown", refused.Head.Decision)

[<Fact>]
let ``hierarchy changed partial or cyclic reads cannot qualify delivery`` () =
    for mode in ["denied"; "partial"; "cycle"; "moved"; "classification"; "null"; "identity"; "errors"; "missing-closure"] do
        let mutable hierarchyReads = 0
        let mutable classificationReads = 0
        let change request response =
            if request.Subject = "selected native work hierarchy" then
                hierarchyReads <- hierarchyReads + 1
                if mode = "denied" then Error(Http(403, "inert denied"))
                else response |> Result.map (fun (value: Transport.Response) ->
                    let body =
                        if mode = "missing-closure" then
                            let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
                            let child = root.["data"].["repository"].["issue"].["subIssues"].["nodes"].[0]
                            child.["state"] <- System.Text.Json.Nodes.JsonValue.Create "CLOSED"
                            child.["closedAt"] <- System.Text.Json.Nodes.JsonValue.Create "2026-10-17T12:00:00Z"
                            root.ToJsonString()
                        elif mode = "null" then value.Body.Replace("\"subIssues\":{", "\"subIssues\":null,\"unused\":{")
                        elif mode = "identity" then value.Body.Replace("\"id\":\"I_outcome\"", "\"id\":\"I_foreign\"")
                        elif mode = "errors" then "{\"data\":null,\"errors\":[{\"message\":\"inert partial native response\"}]}"
                        elif mode = "partial" then value.Body.Replace("\"totalCount\":1", "\"totalCount\":2")
                        elif mode = "cycle" then value.Body.Replace("I_unselected-child", "I_outcome")
                        elif mode = "moved" && hierarchyReads > 1 then value.Body.Replace("I_unselected-child", "I_changed-child")
                        else value.Body
                    {value with Body = body})
            elif request.Subject = "bound V2 successor planning memberships" then
                // The Work level classification rides on the planning membership read (#5119).
                classificationReads <- classificationReads + 1
                response |> Result.map (fun (value: Transport.Response) ->
                    if mode = "classification" && classificationReads > 1 then
                        {value with Body = value.Body.Replace("Delivery", "Outcome").Replace("delivery-option", "outcome-option")}
                    else value)
            else response
        let reads = ProgrammeFixture.successorTransport (ProgrammeFixture.successorBinding ()) (Some "Delivery") change
        let report = V2ProjectionSource.inspectFixed reads (ProgrammeFixture.successorBinding ()) None
        Assert.False(report.Items.Head.WorkItem.Value.Complete, mode)
        Assert.True(report.Items.Head.WorkItem.Value.Sha256.IsNone, mode)
        let result = V2ProjectionSource.selectActions report (Some(ProgrammeFixture.context report)) report.NativeIntegrator.ObservedAt
        Assert.Equal("unknown", result.Head.Decision)
        Assert.Empty reads.Mutations

[<Fact>]
let ``completed source delivery retains parent promise and cannot manufacture acceptance`` () =
    let selected = ProgrammeFixture.successorBinding ()
    let change (request: Transport.Request) response =
        response |> Result.map (fun (value: Transport.Response) ->
            if request.Subject.EndsWith("native observation", StringComparison.Ordinal) || request.Subject = "selected native work hierarchy" then
                let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
                let issue = root.["data"].["repository"].["issue"]
                issue.["state"] <- System.Text.Json.Nodes.JsonValue.Create "CLOSED"
                if request.Subject = "selected native work hierarchy" then
                    issue.["stateReason"] <- System.Text.Json.Nodes.JsonValue.Create "COMPLETED"
                    issue.["closedAt"] <- System.Text.Json.Nodes.JsonValue.Create "2026-10-17T12:00:00Z"
                {value with Body = root.ToJsonString()}
            else value)
    let reads = ProgrammeFixture.successorTransport selected (Some "Delivery") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    let work = report.Items.Head.WorkItem.Value
    Assert.True(work.Complete, string work.Gap)
    Assert.Equal(Some "COMPLETED", work.StateReason)
    Assert.Equal(Some "2026-10-17T12:00:00Z", work.ClosedAt)
    Assert.Equal("I_parent", work.Parent.Value.NodeId)
    Assert.Contains("Completed native issue and human Status disagree", work.ClosureGaps)
    Assert.Contains("Scoped owner acceptance and applicability of all parent requirements are unverified", work.ClosureGaps)
    let result = V2ProjectionSource.selectActions report (Some(ProgrammeFixture.context report)) report.NativeIntegrator.ObservedAt
    Assert.Equal("needs-decision", result.Head.Decision)
    Assert.Empty reads.Mutations

[<Fact>]
let ``done and completed parent discrepancies never use child count as acceptance`` () =
    let selected = ProgrammeFixture.successorBinding ()
    let change (request: Transport.Request) response =
        response |> Result.map (fun (value: Transport.Response) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
            if request.Subject.EndsWith("native observation", StringComparison.Ordinal) || request.Subject = "selected native work hierarchy" then
                let issue = root.["data"].["repository"].["issue"]
                issue.["state"] <- System.Text.Json.Nodes.JsonValue.Create "CLOSED"
                if request.Subject = "selected native work hierarchy" then
                    issue.["stateReason"] <- System.Text.Json.Nodes.JsonValue.Create "COMPLETED"
                    issue.["closedAt"] <- System.Text.Json.Nodes.JsonValue.Create "2026-10-17T12:00:00Z"
                {value with Body = root.ToJsonString()}
            elif request.Subject = "bound V2 successor planning memberships" then
                let status = root.["data"].["repository"].["issue"].["projectItems"].["nodes"].[0].["status"]
                status.["name"] <- System.Text.Json.Nodes.JsonValue.Create "Done"
                status.["optionId"] <- System.Text.Json.Nodes.JsonValue.Create "Done"
                {value with Body = root.ToJsonString()}
            else value)
    let reads = ProgrammeFixture.successorTransport selected (Some "Outcome") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    let work = report.Items.Head.WorkItem.Value
    Assert.Contains("Human Status Done has no scoped acceptance qualification", work.ClosureGaps)
    Assert.Contains("Completed parent has open child scope; required-scope applicability remains unverified", work.ClosureGaps)
    Assert.Empty reads.Mutations


[<Fact>]
let ``not planned or duplicate parent is never described as completed by open children`` () =
    let selected = ProgrammeFixture.successorBinding ()
    for reason in ["NOT_PLANNED"; "DUPLICATE"] do
        let change (request: Transport.Request) response =
            response |> Result.map (fun (value: Transport.Response) ->
                if request.Subject.EndsWith("native observation", StringComparison.Ordinal) || request.Subject = "selected native work hierarchy" then
                    let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
                    let native = root.["data"].["repository"].["issue"]
                    native.["state"] <- System.Text.Json.Nodes.JsonValue.Create "CLOSED"
                    if request.Subject = "selected native work hierarchy" then
                        native.["stateReason"] <- System.Text.Json.Nodes.JsonValue.Create reason
                        native.["closedAt"] <- System.Text.Json.Nodes.JsonValue.Create "2026-10-17T12:00:00Z"
                    {value with Body = root.ToJsonString()}
                else value)
        let reads = ProgrammeFixture.successorTransport selected (Some "Outcome") change
        let report = V2ProjectionSource.inspectFixed reads selected None
        let work = report.Items.Head.WorkItem.Value
        Assert.True(work.Complete, string work.Gap)
        Assert.Equal(Some reason, work.StateReason)
        Assert.DoesNotContain(work.ClosureGaps, fun gap -> gap.StartsWith("Completed parent", StringComparison.Ordinal))
        Assert.Contains("Scoped owner acceptance and applicability of all parent requirements are unverified", work.ClosureGaps)
        Assert.Empty reads.Mutations

[<Fact>]
let ``existing delivery PR observation retains exact owner and consumes no new capacity`` () =
    let selected = ProgrammeFixture.successorBinding ()
    let nativePr = ProgrammeFixture.transport ProgrammeFixture.unchanged
    let change (request: Transport.Request) response =
        if request.Subject.EndsWith("open PR inventory", StringComparison.Ordinal) then
            (nativePr :> IGitHubTransport).Send request
        elif request.Subject.EndsWith("native observation", StringComparison.Ordinal) || request.Subject = "selected native work hierarchy" then
            response |> Result.map (fun (value: Transport.Response) ->
                {value with Body = value.Body.Replace("None", "https://github.com/FS-GG/.github/pull/42")})
        else response
    let reads = ProgrammeFixture.successorTransport selected (Some "Delivery") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    let supplied = ProgrammeFixture.context report
    Assert.Equal(V2ProjectionSource.ObserveExisting, supplied.Slices.Head.Operation)
    let context = {supplied with ImplementationSlots = None; ReviewSlots = None; HostedCiSlots = None; CapacityObservation = None; Reservations = None; ReservationObservation = None}
    let result = V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt
    Assert.Equal("continue-existing", result.Head.Decision)
    let wrongOwner = {context with Slices = [{context.Slices.Head with AccountableOwner = "other-owner"}]}
    Assert.Equal("needs-decision", (V2ProjectionSource.selectActions report (Some wrongOwner) report.NativeIntegrator.ObservedAt).Head.Decision)
    Assert.Equal("owner-attempt", result.Head.Attempt.Value)
    Assert.Empty reads.Mutations

[<Fact>]
let ``complete selected hierarchy cycle refuses affected nodes without blocking independent delivery`` () =
    let rows =
        [ "I_independent", {ProgrammeFixture.issue with Number = 6000}, "docs/coordination/programme.md"
          "I_outcome", ProgrammeFixture.issue, "docs/coordination/programme.md"
          "I_b", {ProgrammeFixture.issue with Number = 6001}, "docs/coordination/programme.md"
          "I_c", {ProgrammeFixture.issue with Number = 6002}, "docs/coordination/programme.md" ]
    let selected =
        {ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList}
        |> fun binding -> ProgrammeFixture.enroll binding rows
    let change (request: Transport.Request) response =
        if request.Subject <> "selected native work hierarchy" then response
        else response |> Result.map (fun (value: Transport.Response) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
            let native = root.["data"].["repository"].["issue"]
            let id = native.["id"].GetValue<string>()
            let parent, child =
                match id with
                | "I_outcome" -> "I_c", "I_b"
                | "I_b" -> "I_outcome", "I_c"
                | "I_c" -> "I_b", "I_outcome"
                | _ -> "I_parent", "I_unselected-child"
            let number id fallback = rows |> List.tryPick (fun (node, issue, _) -> if node = id then Some issue.Number else None) |> Option.defaultValue fallback
            native.["parent"].["id"] <- System.Text.Json.Nodes.JsonValue.Create parent
            native.["parent"].["number"] <- System.Text.Json.Nodes.JsonValue.Create(number parent 9998)
            native.["subIssues"].["nodes"].[0].["id"] <- System.Text.Json.Nodes.JsonValue.Create child
            native.["subIssues"].["nodes"].[0].["number"] <- System.Text.Json.Nodes.JsonValue.Create(number child 9999)
            {value with Body = root.ToJsonString()})
    let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    for item in report.Items do
        if item.ExpectedNodeId = "I_independent" then Assert.True(item.WorkItem.Value.Complete, string item.WorkItem.Value.Gap)
        else
            Assert.False(item.WorkItem.Value.Complete)
            Assert.True(item.WorkItem.Value.Sha256.IsNone)
    let context = ProgrammeFixture.context report
    let result = V2ProjectionSource.selectActions report (Some context) report.NativeIntegrator.ObservedAt
    Assert.Equal("ready", result.Head.Decision)
    Assert.Equal("I_independent", context.Slices.Head.Join.NativeId)
    Assert.Equal({ProgrammeFixture.issue with Number = 6000}, result.Head.Issue)
    Assert.Equal(Some context.Slices.Head.Owner, result.Head.Owner)
    Assert.Equal(Some context.Slices.Head.Attempt, result.Head.Attempt)
    Assert.Equal(Some V2ProjectionSource.Implement, result.Head.Operation)
    Assert.Contains(result.Head.Reasons, fun reason -> reason.Contains("action-specific remaining capacity"))
    for affected in report.Items |> List.filter (fun item -> item.ExpectedNodeId <> "I_independent") do
        let scoped = {report with Items = affected :: (report.Items |> List.filter (fun item -> item.ExpectedNodeId <> affected.ExpectedNodeId))}
        let refusal = V2ProjectionSource.selectActions scoped (Some(ProgrammeFixture.context scoped)) report.NativeIntegrator.ObservedAt
        Assert.Equal(affected.Evidence.Issue, refusal.Head.Issue)
        Assert.Equal("unknown", refusal.Head.Decision)
    Assert.Empty reads.Mutations

[<Fact>]
let ``selected hierarchy joins exact issue and node pairs while independent delivery stays ready`` () =
    let b = {ProgrammeFixture.issue with Number = 6001}
    let independent = {ProgrammeFixture.issue with Number = 6000}
    let rows =
        [ "I_independent", independent, "docs/coordination/programme.md"
          "I_outcome", ProgrammeFixture.issue, "docs/coordination/programme.md"
          "I_b", b, "docs/coordination/programme.md" ]
    let selected =
        {ProgrammeFixture.successorBinding () with SelectedIssues = rows |> List.map (fun (id, issue, _) -> id, $"{issue.Owner}/{issue.Repository}#{issue.Number}") |> Map.ofList}
        |> fun binding -> ProgrammeFixture.enroll binding rows
    for mode in ["unchanged"; "child-pair"; "parent-pair"; "child-reverse"; "parent-reverse"] do
        let change (request: Transport.Request) response =
            if request.Subject <> "selected native work hierarchy" then response
            else response |> Result.map (fun (value: Transport.Response) ->
                let root = System.Text.Json.Nodes.JsonNode.Parse value.Body
                let native = root.["data"].["repository"].["issue"]
                let id = native.["id"].GetValue<string>()
                let setIdentity (node: System.Text.Json.Nodes.JsonNode) nodeId repository number =
                    node.["id"] <- System.Text.Json.Nodes.JsonValue.Create(nodeId: string)
                    node.["number"] <- System.Text.Json.Nodes.JsonValue.Create(number: int)
                    node.["repository"].["nameWithOwner"] <- System.Text.Json.Nodes.JsonValue.Create(repository: string)
                let parent = native.["parent"]
                let child = native.["subIssues"].["nodes"].[0]
                if id = "I_outcome" then
                    setIdentity child "I_b" "FS-GG/.github" b.Number
                    if mode = "child-pair" then setIdentity child "I_b" "Other/Repo" 77
                    if mode = "child-reverse" then setIdentity child "I_wrong" "FS-GG/.github" b.Number
                elif id = "I_b" then
                    setIdentity parent "I_outcome" "FS-GG/.github" ProgrammeFixture.issue.Number
                    setIdentity child "I_b-external-child" "FS-GG/.github" 9997
                    if mode = "parent-pair" then setIdentity parent "I_outcome" "Other/Repo" 77
                    if mode = "parent-reverse" then setIdentity parent "I_wrong" "FS-GG/.github" ProgrammeFixture.issue.Number
                else setIdentity child "I_independent-external-child" "FS-GG/.github" 9996
                {value with Body = root.ToJsonString()})
        let reads = ProgrammeFixture.successorTransportFor selected rows (Some "Delivery") change
        let report = V2ProjectionSource.inspectFixed reads selected None
        for item in report.Items do
            if mode = "unchanged" || item.ExpectedNodeId = "I_independent" then
                Assert.True(item.WorkItem.Value.Complete, mode + ": " + string item.WorkItem.Value.Gap)
            else
                Assert.False(item.WorkItem.Value.Complete, mode)
                Assert.True(item.WorkItem.Value.Sha256.IsNone)
                let scoped = {report with Items = item :: (report.Items |> List.filter (fun other -> other.ExpectedNodeId <> item.ExpectedNodeId))}
                Assert.Equal("unknown", (V2ProjectionSource.selectActions scoped (Some(ProgrammeFixture.context scoped)) report.NativeIntegrator.ObservedAt).Head.Decision)
        let result = V2ProjectionSource.selectActions report (Some(ProgrammeFixture.context report)) report.NativeIntegrator.ObservedAt
        Assert.Equal("ready", result.Head.Decision)
        Assert.Equal(independent, result.Head.Issue)
        Assert.Empty reads.Mutations

let private successorPlanningPages mode =
    let selected = ProgrammeFixture.successorBinding ()
    let change (request: Transport.Request) result =
        if request.Subject <> "bound V2 successor planning memberships" then result
        else result |> Result.map (fun (response: Transport.Response) ->
            let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
            let page = root.["data"].["repository"].["issue"].["projectItems"]
            let target = page.["nodes"].[0]
            let after = match request.Body with Query(_, variables) -> variables |> List.exists (function "after", _ -> true | _ -> false) | _ -> false
            let paginated = mode = "pages" || mode = "owner-on-page-two"
            if paginated then
                page.["totalCount"] <- System.Text.Json.Nodes.JsonValue.Create 2
                page.["pageInfo"].["hasNextPage"] <- System.Text.Json.Nodes.JsonValue.Create(not after)
                page.["pageInfo"].["endCursor"] <- System.Text.Json.Nodes.JsonValue.Create(if after then "last" else "next")
                if not after then
                    target.["id"] <- System.Text.Json.Nodes.JsonValue.Create "PVTI_other"
                    target.["project"].["id"] <- System.Text.Json.Nodes.JsonValue.Create "PVT_other"
                    for field in ["status"; "track"; "roadmap"; "observation"] do target.AsObject().Remove field |> ignore
                elif mode = "owner-on-page-two" then
                    root.["data"].["organization"].["id"] <- System.Text.Json.Nodes.JsonValue.Create "O_wrong"
            match mode with
            | "duplicate-target" ->
                let duplicate = target.DeepClone()
                duplicate.["id"] <- System.Text.Json.Nodes.JsonValue.Create "PVTI_duplicate"
                page.["nodes"].AsArray().Add duplicate
                page.["totalCount"] <- System.Text.Json.Nodes.JsonValue.Create 2
            | "partial-page" -> page.["pageInfo"].["hasNextPage"] <- System.Text.Json.Nodes.JsonValue.Create true
            | "field-id" -> target.["status"].["field"].["id"] <- System.Text.Json.Nodes.JsonValue.Create "wrong"
            | "field-type" -> target.["track"].["__typename"] <- System.Text.Json.Nodes.JsonValue.Create "ProjectV2ItemFieldTextValue"
            | "option-id" -> target.["observation"].["optionId"] <- System.Text.Json.Nodes.JsonValue.Create "wrong"
            | "missing-field" -> target.AsObject().Remove "roadmap" |> ignore
            | "content-id" -> target.["content"].["id"] <- System.Text.Json.Nodes.JsonValue.Create "I_wrong"
            | _ -> ()
            { response with Body = root.ToJsonString() })
    selected, ProgrammeFixture.successorTransport selected (Some "Delivery") change

[<Fact>]
let ``successor planning drains membership pages and ignores other project planning values`` () =
    let selected, reads = successorPlanningPages "pages"
    match readPlanning reads selected {Issue = ProgrammeFixture.issue; ExpectedNodeId = "I_outcome"} with
    | Error error -> failwith (Errors.explain error)
    | Ok planning ->
        Assert.Equal(2, planning.MembershipPages)
        Assert.Equal("synthetic-member-I_outcome", planning.ItemId)
        Assert.Equal(Some "Blocked", planning.Status)
        Assert.Equal(Some "Unknown", planning.Observation)
    Assert.Empty reads.Mutations

[<Theory>]
[<InlineData("owner-on-page-two")>]
[<InlineData("duplicate-target")>]
[<InlineData("partial-page")>]
[<InlineData("field-id")>]
[<InlineData("field-type")>]
[<InlineData("option-id")>]
[<InlineData("missing-field")>]
[<InlineData("content-id")>]
let ``successor planning refuses incomplete or conflicting native facts`` mode =
    let selected, reads = successorPlanningPages mode
    Assert.True(Result.isError(readPlanning reads selected {Issue = ProgrammeFixture.issue; ExpectedNodeId = "I_outcome"}), mode)
    Assert.Empty reads.Mutations

[<Fact>]
let ``successor planning changes at the final barrier remain Unknown`` () =
    let selected = ProgrammeFixture.successorBinding ()
    let mutable planningReads = 0
    let change (request: Transport.Request) result =
        if request.Subject <> "bound V2 successor planning memberships" then result
        else
            planningReads <- planningReads + 1
            if planningReads = 1 then result
            else result |> Result.map (fun (response: Transport.Response) ->
                let root = System.Text.Json.Nodes.JsonNode.Parse response.Body
                let track = root.["data"].["repository"].["issue"].["projectItems"].["nodes"].[0].["track"]
                track.["name"] <- System.Text.Json.Nodes.JsonValue.Create "Follow-up"
                track.["optionId"] <- System.Text.Json.Nodes.JsonValue.Create "follow"
                { response with Body = root.ToJsonString() })
    let reads = ProgrammeFixture.successorTransport selected (Some "Delivery") change
    let report = V2ProjectionSource.inspectFixed reads selected None
    Assert.Equal(2, planningReads)
    Assert.Equal("Unknown", report.Items.Head.SourceCurrentness)
    Assert.Equal("Unknown", report.Items.Head.Evidence.Health)
    Assert.Empty reads.Mutations
