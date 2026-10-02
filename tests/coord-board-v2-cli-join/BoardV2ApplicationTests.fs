module FS.GG.Coord.GitHub.Tests.BoardV2ApplicationTests

open System
open System.IO
open System.Text.Json
open System.Security.Cryptography
open Xunit
open FS.GG.Coord.Cli
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Transport
open FS.GG.Coord.GitHub.V2Projection
open FS.GG.Coord.GitHub.V2ProjectionSource

let private bindingJson artifact =
    JsonSerializer.Serialize
        {| schemaVersion = 1; recipeRevision = String.replicate 40 "a"; populationRevision = String.replicate 40 "c"
           organizationId = "O_fsgg"; artifactSha256 = artifact; ownerKind = "organization"; owner = "FS-GG"
           projectNumber = 77; projectTitle = "Coordination V2"; projectId = "PVT_coord_v2"
           status = {| id = "status"; options = Map.ofList ["Backlog", "b"; "Ready", "r"; "In progress", "i"; "Blocked", "x"; "Done", "d"] |}
           roadmapFieldId = "roadmap"; track = {| id = "track"; options = Map.ofList ["Active delivery", "a"; "Follow-up", "f"] |}
           observation = {| id = "observation"; options = Map.ofList ["Verified", "v"; "Stale", "s"; "Unknown", "u"] |}
           repositories = ["FS-GG/.github"] |}

let private loadedDigest () =
    use stream = File.OpenRead typeof<Binding>.Assembly.Location
    SHA256.HashData stream |> Convert.ToHexString |> _.ToLowerInvariant()

[<Fact>]
let ``binding refuses caller currentness and executable inputs`` () =
    let valid = bindingJson (loadedDigest ())
    Assert.True(BoardV2Application.parseBinding valid |> Result.isOk)
    for extra in ["current"; "verifier"; "recipe"; "url"] do
        let spoofed = valid.Insert(1, sprintf "\"%s\":\"Current\"," extra)
        Assert.True(BoardV2Application.parseBinding spoofed |> Result.isError)
    Assert.True(BoardV2Application.parseBinding (valid.Replace("organization", "user")) |> Result.isError)
    Assert.True(BoardV2Application.parseBinding (valid.Replace("Coordination V2", "Coordination")) |> Result.isError)

[<Fact>]
let ``invalid loaded artifact and unknown flags perform no transport calls or report writes`` () =
    let directory = Path.Combine(Path.GetTempPath(), "board-v2-cli-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory directory |> ignore
    try
        let bindingPath = Path.Combine(directory, "binding.json")
        let reportPath = Path.Combine(directory, "report.json")
        File.WriteAllText(bindingPath, bindingJson (String.replicate 64 "0"))
        let mutable calls = 0
        let transport =
            { new IGitHubTransport with
                member _.Send request = calls <- calls + 1; Error(Errors.Unauthorized request.Subject)
                member _.SendMutation mutation = calls <- calls + 1; Error(Errors.Unauthorized mutation.Request.Subject)
                member _.RetryMutation effect = calls <- calls + 1; Error(Errors.Unauthorized effect) }
        let args = ["refresh"; "--binding-file"; bindingPath; "--report-file"; reportPath]
        Assert.Equal(1, BoardV2Application.runWithTransport transport args)
        Assert.Equal(1, BoardV2Application.runWithTransport transport (args @ ["--current"; "true"]))
        Assert.Equal(0, calls)
        Assert.False(File.Exists reportPath)
    finally Directory.Delete(directory, true)

[<Fact>]
let ``report history roundtrip strips current authority while retaining last verification`` () =
    let issue = { Owner = "FS-GG"; Repository = ".github"; Number = 2963 }
    let projection =
        { ProjectId = "PVT_coord_v2"; RecipeRevision = String.replicate 40 "a"; Issue = issue
          ObservedRevision = "native-observed"; Observation = "Verified"; Outcome = Updated "PVTI_native"
          SourceChecks = 2; ProjectReads = 5; Mutations = 1; VerifiedAt = DateTimeOffset.Parse "2026-10-02T01:02:03Z" }
    let report =
        { ProjectId = projection.ProjectId; RecipeRevision = projection.RecipeRevision; PopulationRevision = String.replicate 40 "c"
          Selected = Some 1; Attempted = 1; Verified = 1; PopulationGap = None; Cleanup = "complete"
          Items = [ { Issue = issue; Native = None; DependencyObservations = []; Delivery = "Unknown"; Publication = "Unknown"
                      NativeAcceptance = "Unknown"; Health = "Verified"; Reads = 5; MutationAttempts = 1; MembershipPages = Some 1
                      Projection = Some projection; Gap = None; LastVerified = Some projection } ] }
    let wire = BoardV2Application.encodeReport report
    match BoardV2Application.decodePreviousReport wire with
    | Error error -> failwith error
    | Ok history ->
        Assert.Equal(0, history.Verified)
        Assert.True(history.Selected.IsNone)
        Assert.True(history.Items.Head.Projection.IsNone)
        Assert.Equal("Unknown", history.Items.Head.Health)
        Assert.Equal(Some projection, history.Items.Head.LastVerified)
    let failedRefresh =
        { report with
            RecipeRevision = String.replicate 40 "b"
            Verified = 0
            PopulationGap = Some(Errors.Unauthorized "protected population")
            Items = report.Items |> List.map (fun item ->
                { item with Health = "Unknown"; Projection = None; Gap = Some(Errors.Unauthorized "native issue") }) }
    let failedWire = BoardV2Application.encodeReport failedRefresh
    match BoardV2Application.decodePreviousReport failedWire with
    | Error error -> failwith error
    | Ok history ->
        Assert.Equal(String.replicate 40 "b", history.RecipeRevision)
        Assert.Equal(Some projection, history.Items.Head.LastVerified)
        Assert.True(history.Items.Head.Projection.IsNone)
        Assert.Equal("Unknown", history.Items.Head.Health)
    Assert.True(BoardV2Application.decodePreviousReport (failedWire.Replace(String.replicate 40 "a", "caller-current")) |> Result.isError)
    Assert.True(BoardV2Application.decodePreviousReport (wire.Replace("\"outcome\":\"updated\"", "\"outcome\":\"Current\"")) |> Result.isError)
