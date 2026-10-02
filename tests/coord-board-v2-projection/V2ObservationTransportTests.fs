module FS.GG.Coord.GitHub.Tests.V2ObservationTransportTests

open System
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V2Projection

let private binding =
    { BindingVersion = 2; SchemaVersion = 1; ImportRecipeRevision = String.replicate 40 "e"; ImportArtifactSha256 = String.replicate 64 "f"
      RecipeRevision = String.replicate 40 "a"; PopulationRevision = String.replicate 40 "c"; ArtifactSha256 = String.replicate 64 "b"
      OrganizationId = "O_kgDOEYAWYw"; OwnerKind = Org; Owner = "FS-GG"; ProjectNumber = 3; ProjectTitle = "Coordination V2"; ProjectId = "PVT_kwDOEYAWY84Bldpa"
      Status = { Id = "status"; Options = Map.ofList [ "Backlog", "b"; "Ready", "r"; "In progress", "i"; "Blocked", "x"; "Done", "d" ] }
      RoadmapFieldId = "roadmap"; Track = { Id = "track"; Options = Map.ofList [ "Active delivery", "a"; "Follow-up", "f" ] }
      Observation = { Id = "PVTSSF_lADOEYAWY84BldpazhkKrUw"; Options = Map.ofList [ "Verified", "c0e289aa"; "Stale", "0a28b459"; "Unknown", "ab03cd87" ] }
      Repositories = Set.ofList [ "FS-GG/.github"; "FS-GG/FS.GG.SDD"; "FS-GG/FS.GG.Templates" ]
      SelectedIssues = Map.ofList [ "I_kwDOS_PboM8AAAABOUVZAw", "FS-GG/FS.GG.SDD#928"; "I_kwDOTGkvVs8AAAABOUV4AA", "FS-GG/FS.GG.Templates#441"; "I_kwDOS6feoM8AAAABOUWpiA", "FS-GG/.github#3010" ] }

let private response body = Ok { Status = 200; Body = body; ETag = None; NextLink = None; Headers = Map.empty }
let private identity = """{"data":{"node":{"id":"item928","project":{"id":"PVT_kwDOEYAWY84Bldpa"},"content":{"id":"I_kwDOS_PboM8AAAABOUVZAw","number":928,"repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}}}"""
let private board: Board.BoardMap =
    { Number = 3; Id = binding.ProjectId; Owner = binding.Owner; Title = binding.ProjectTitle
      Fields = Map.ofList [ "Observation", { Id = binding.Observation.Id; Type = Board.SingleSelect binding.Observation.Options } ] }

[<Fact>]
let ``real field constructor dispatches through exact guard once after native identity read`` () =
    let mutable order = []
    let reads = Fake.Recorder(fun _ -> order <- order @ [ "identity" ]; response identity)
    let mutable emitted = None
    let native request = order <- order @ [ "dispatch" ]; emitted <- Some request; response """{"data":{"updateProjectV2ItemFieldValue":{"clientMutationId":null}}}"""
    let transport = V2ObservationTransport.compose binding reads native |> Result.defaultWith (fun error -> failwith (Errors.explain error))
    Assert.True(Board.setField transport board "item928" "Observation" (Board.Set "Verified") |> Result.isOk)
    Assert.Equal<string list>([ "identity"; "dispatch" ], order)
    Assert.True(emitted.IsSome)
    Assert.True(transport.RetryMutation "arbitrary" |> Result.isError)
    Assert.Equal(2, order.Length)

[<Fact>]
let ``foreign item content including unapproved935 never reaches native dispatch`` () =
    for bad in [ identity.Replace("PVT_kwDOEYAWY84Bldpa", "foreign-project")
                 identity.Replace("I_kwDOS_PboM8AAAABOUVZAw", "I_kwDOS_PboM8AAAABOda86Q").Replace("928", "935")
                 identity.Replace("FS-GG/FS.GG.SDD", "foreign/repository")
                 identity.Replace("item928", "different-item") ] do
        let reads = Fake.Recorder(fun _ -> response bad)
        let mutable writes = 0
        let transport = V2ObservationTransport.compose binding reads (fun _ -> writes <- writes + 1; response "{}") |> Result.defaultWith (fun error -> failwith (Errors.explain error))
        Assert.True(Board.setField transport board "item928" "Observation" (Board.Set "Verified") |> Result.isError)
        Assert.Equal(0, writes)

[<Fact>]
let ``closed mutation shape rejects fields options alias batches and arbitrary documents`` () =
    let mutable original = None
    let capture = Fake.Recorder(fun _ -> response "{}")
    let capturing =
        { new IGitHubTransport with
            member _.Send request = (capture :> IGitHubTransport).Send request
            member _.RetryMutation _ = failwith "unexpected"
            member _.SendMutation intent = original <- Some intent; response """{"data":{"ok":{}}}""" }
    Board.setField capturing board "item928" "Observation" (Board.Set "Verified") |> ignore
    let intent = original.Value
    let body = match intent.Request.Body with Query(document, variables) -> document, variables | _ -> failwith "expected typed GraphQL"
    let documents = [ fst body + " alias: archiveProjectV2Item"; "mutation { addProjectV2ItemById { clientMutationId } }"; "mutation { clearProjectV2ItemFieldValue { clientMutationId } }" ]
    for document in documents do
        Assert.True(V2ObservationTransport.authorize binding { intent with Request = { intent.Request with Body = Query(document, snd body) } } |> Result.isError)
    for key, value in [ "projectId", VId "other"; "fieldId", VId "status"; "optionId", VString "ab03cd87"; "itemId", VId "" ] do
        let variables = snd body |> List.map (fun (name, original) -> name, if name = key then value else original)
        Assert.True(V2ObservationTransport.authorize binding { intent with Request = { intent.Request with Body = Query(fst body, variables) } } |> Result.isError)
    Assert.True(V2ObservationTransport.authorize binding { intent with Request = { intent.Request with Body = Query(fst body, snd body @ [ "extra", VString "Current" ]) } } |> Result.isError)

[<Fact>]
let ``unselected target binding or cohort cannot construct authorized composition`` () =
    for bad in [ { binding with ProjectId = "other" }; { binding with Observation = { binding.Observation with Id = "status" } }; { binding with SelectedIssues = Map.add "I_kwDOS_PboM8AAAABOda86Q" "FS-GG/FS.GG.SDD#935" binding.SelectedIssues } ] do
        Assert.True(V2ObservationTransport.validateScope bad |> Result.isError)

[<Fact>]
let ``lost native mutation response is returned without a repeated dispatch`` () =
    let reads = Fake.Recorder(fun _ -> response identity)
    let mutable writes = 0
    let native _ = writes <- writes + 1; Error(Errors.Transport "response unavailable")
    let transport = V2ObservationTransport.compose binding reads native |> Result.defaultWith (fun error -> failwith (Errors.explain error))
    match Board.setField transport board "item928" "Observation" (Board.Set "Verified") with
    | Error(Errors.Transport "response unavailable") -> ()
    | other -> failwith $"lost native result must remain unresolved: {other}"
    Assert.Equal(1, writes)
    Assert.True(transport.RetryMutation "lost" |> Result.isError)
    Assert.Equal(1, writes)
