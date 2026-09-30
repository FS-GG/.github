module FS.GG.Coord.GitHub.Tests.V2ProjectionTests

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
        SchemaVersion = 1
        RecipeRevision = "reviewed-recipe-sha"
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

let private request source = { Issue = issue; Source = source }

let private exactProject projectId observationFieldId =
    $"""{{"data":{{"organization":{{"login":"FS-GG","projectV2":{{"id":"{projectId}","number":77,"title":"Coordination V2","fields":{{"totalCount":4,"nodes":[
      {{"id":"PVTSSF_status_v2","name":"Status","dataType":"SINGLE_SELECT","options":[{{"id":"status_backlog","name":"Backlog"}},{{"id":"status_ready","name":"Ready"}},{{"id":"status_wip","name":"In progress"}},{{"id":"status_blocked","name":"Blocked"}},{{"id":"status_done","name":"Done"}}]}},
      {{"id":"PVTF_roadmap_v2","name":"Roadmap","dataType":"TEXT"}},
      {{"id":"PVTSSF_track_v2","name":"Track","dataType":"SINGLE_SELECT","options":[{{"id":"track_active","name":"Active delivery"}},{{"id":"track_followup","name":"Follow-up"}}]}},
      {{"id":"{observationFieldId}","name":"Observation","dataType":"SINGLE_SELECT","options":[{{"id":"observation_verified","name":"Verified"}},{{"id":"observation_stale","name":"Stale"}},{{"id":"observation_unknown","name":"Unknown"}}]}}
    ]}}}}}}}}}}"""

let private target = ok (exactProject binding.ProjectId binding.Observation.Id)

let private membership =
    ok """{"data":{"repository":{"issue":{"projectItems":{"totalCount":1,"nodes":[{"id":"PVTI_2963","project":{"number":77}}]}}}}}"""

let private observation value =
    match value with
    | Some name -> ok $"""{{"data":{{"node":{{"fieldValueByName":{{"name":"{name}"}}}}}}}}"""
    | None -> ok """{"data":{"node":{"fieldValueByName":null}}}"""

let private mutationSuccess =
    ok """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""

let private scripted (responses: IoResult<Response> list) =
    let queue = Queue<IoResult<Response>>(responses)

    Fake.Recorder(fun _ ->
        if queue.Count = 0 then failwith "the adapter exceeded its bounded fixture"
        else queue.Dequeue())

[<Fact>]
let ``one shot writes only the reviewed Observation field`` () =
    let transport = scripted [ target; membership; observation (Some "Stale"); mutationSuccess ]

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Ok report ->
        Assert.Equal(Updated "PVTI_2963", report.Outcome)
        Assert.Equal(3, report.Reads)
        Assert.Equal(1, report.Mutations)
        Assert.Equal(4, transport.GraphQlCalls)
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
            [ target; membership; observation None; mutationSuccess
              target; membership; observation (Some "Verified") ]

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Ok { Outcome = Updated _ } -> ()
    | other -> failwith $"first pass must update, got %A{other}"

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Ok report ->
        Assert.Equal(AlreadyCurrent "PVTI_2963", report.Outcome)
        Assert.Equal(0, report.Mutations)
        Assert.Single(transport.Mutations) |> ignore
    | other -> failwith $"retry must converge without a duplicate mutation, got %A{other}"

[<Fact>]
let ``lost response remains unknown and retry first resolves live state`` () =
    let transport =
        scripted
            [ target; membership; observation None; Error(Transport "response lost")
              target; membership; observation (Some "Verified") ]

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Error(Transport "response lost") -> ()
    | other -> failwith $"a lost response must not be reported applied, got %A{other}"

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
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
        let transport = scripted responses

        match runOneShot transport binding (request (Fresh "issue-etag-1")) with
        | Error _ -> Assert.Empty(transport.Mutations)
        | Ok report -> failwith $"unsafe target read produced a projection report: %A{report}"

[<Fact>]
let ``incomplete membership pagination cannot become absence or license a mutation`` () =
    let incomplete =
        ok """{"data":{"repository":{"issue":{"projectItems":{"totalCount":21,"nodes":[]}}}}}"""

    let transport = scripted [ target; incomplete ]

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Error(Malformed _) ->
        Assert.Equal(2, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | other -> failwith $"an incomplete connection must fail closed, got %A{other}"

[<Theory>]
[<InlineData("stale")>]
[<InlineData("unknown")>]
[<InlineData("foreign")>]
let ``stale unknown and foreign source requests fail before target IO`` caseName =
    let transport = scripted []

    let candidate =
        match caseName with
        | "stale" -> request (Stale(Some "issue-etag-0"))
        | "unknown" -> request (Unknown "source read denied")
        | _ ->
            {
                Issue = { issue with Repository = "foreign" }
                Source = Fresh "issue-etag-1"
            }

    match runOneShot transport binding candidate with
    | Error _ ->
        Assert.Equal(0, transport.GraphQlCalls)
        Assert.Empty(transport.Mutations)
    | Ok report -> failwith $"unverified input produced a projection report: %A{report}"

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
    let transport = scripted [ target; membership; observation (Some "Surprising") ]

    match runOneShot transport binding (request (Fresh "issue-etag-1")) with
    | Error(Malformed _) -> Assert.Empty(transport.Mutations)
    | other -> failwith $"an unknown live value must refuse, got %A{other}"
