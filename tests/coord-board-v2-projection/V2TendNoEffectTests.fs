/// Synthetic incident policy and native-read ports; no original private incident artifacts are included.
module FS.GG.Coord.GitHub.Tests.V2TendNoEffectTests

open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord.GitHub
open FS.GG.Coord.GitHub.Tests.TendFakeGitHub

type private Fixture() =
    let root = Path.Combine(Path.GetTempPath(), "tend-settlement-" + Guid.NewGuid().ToString("N"))
    let mode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
    let run = "synthetic-incident"
    let board = Board()
    let custody = V2TendWrite.Custody(Path.Combine(root, "tend"), run, ignore)
    let original = JsonObject()
    let field = JsonObject()
    let mutable held = false
    let mutable schemas = 0
    let mutable schema = "{\"data\":{\"__type\":{\"name\":\"ProjectV2FieldValue\",\"kind\":\"INPUT_OBJECT\",\"inputFields\":[{\"name\":\"singleSelectOptionId\",\"type\":{\"kind\":\"SCALAR\",\"name\":\"String\"}}]}}}"
    do
        Directory.CreateDirectory(root, mode ||| UnixFileMode.UserExecute) |> ignore
        board.AddItem("PVTI_fixture", { ContentId = "I_fixture"; Ref = "FS-GG/fixture#1"; Open = true; StateReason = None;
                                       Parent = None; Values = Map.ofList [ "Status", "In progress" ]; Archived = false })
        original["kind"] <- "fields"; original["state"] <- "possible-send"
        original["itemId"] <- "PVTI_fixture"; original["ref"] <- "FS-GG/fixture#1"
        field["field"] <- "Status"; field["fieldId"] <- fieldId "Status"
        field["preimage"] <- "In progress"; field["target"] <- "In review"; field["value"] <- optionId "Status" "In review"
        original["fields"] <- JsonArray(field)
        custody.Write("PVTI_fixture", original)
    let path = custody.Path "PVTI_fixture"
    let originalBytes = File.ReadAllBytes path
    let request = V2TendFieldRecovery.reconstruct original
    let requestPath = Path.Combine(root, "request.json")
    let runtime = Path.Combine(root, "runtime")
    let assemblies = Map.ofList [ "original.dll", Encoding.UTF8.GetBytes "synthetic qualified original assembly" ]
    let policy : V2TendNoEffect.Policy =
        { Run = run; Attempts = Map.ofList [ V2TendNoEffect.digest originalBytes, V2TendNoEffect.digest request ]
          Assemblies = assemblies |> Map.map (fun _ value -> V2TendNoEffect.digest value) }
    let location = V2WriterLock.inDirectory (Path.Combine(root, "test-locks")) projectId V2WriterLock.migrationLegacyName
    do
        Directory.CreateDirectory runtime |> ignore
        File.WriteAllBytes(requestPath, request)
        for KeyValue(name, value) in assemblies do File.WriteAllBytes(Path.Combine(runtime, name), value)
    member _.Root = root
    member _.Run = run
    member _.Board = board
    member _.Path = path
    member _.OriginalBytes = originalBytes
    member _.Custody = custody
    member _.Policy = policy
    member _.Location = location
    member _.SchemaCalls = schemas
    member _.Schema with get() = schema and set value = schema <- value
    member _.Input : V2TendNoEffect.Input =
        { CustodyRoot = custody.Root; OriginalPath = path; RequestPath = requestPath; OriginalRuntimeDirectory = runtime
          RootAdmissionReference = "https://github.com/FS-GG/.github/issues/5206#issuecomment-1" }
    member _.Dependencies : V2TendNoEffect.Admission =
        { Acquire = fun () ->
              let lease = V2WriterLock.acquire location
              held <- true
              { new IDisposable with member _.Dispose() = held <- false; lease.Dispose() }
          ReadCurrent = fun record -> Assert.True held; V2TendFieldRecovery.readCurrent board.Transport record
          ReadSchema = fun () -> Assert.True held; schemas <- schemas + 1; Ok schema
          Reconstruct = V2TendFieldRecovery.reconstruct
          SyncDirectory = ignore
          Clock = board.Clock }
    member this.Admit() = V2TendNoEffect.admitWith policy this.Dependencies this.Input
    member _.Context : V2TendWrite.Context =
        { Reads = board.Transport; Send = board.Mutate; Custody = V2TendWrite.Custody(custody.Root, "fresh-run", ignore)
          Gate = V2TendWrite.GateCustody(Path.Combine(root, "gate"), ignore); Clock = board.Clock
          Pacer = V2TendWrite.Pacer(board.Clock, ignore, TimeSpan.Zero); Closable = Collections.Generic.HashSet<string>()
          Settings = V2TendWrite.defaultSettings projectId Set.empty }
    member _.Incident path = Path.GetFileName(Path.GetDirectoryName(path: string)) = run
    interface IDisposable with member _.Dispose() = Directory.Delete(root, true)

[<Fact>]
let certificate_is_append_only_private_and_never_rewrites_the_original () =
    use f = new Fixture()
    let certificate = f.Admit() |> Result.defaultWith failwith
    Assert.True(V2TendNoEffect.settledWith f.Policy f.Custody.Root f.Path)
    Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)
    Assert.True(f.Admit() |> Result.isError)
    for path in Directory.GetFiles(Path.GetDirectoryName certificate) do
        Assert.Equal(UnixFileMode.UserRead ||| UnixFileMode.UserWrite, File.GetUnixFileMode path)
    Assert.Equal(0, snd (f.Custody.Records()))
    Assert.Empty f.Board.Mutations

[<Fact>]
let later_valid_writes_do_not_resurrect_the_original_and_sweeps_do_not_touch_it () =
    use f = new Fixture()
    f.Admit() |> Result.defaultWith failwith |> ignore
    f.Board.Items.["PVTI_fixture"].Values <- Map.ofList [ "Status", "In review" ]
    let calls = f.Board.Calls.Length
    let held, outcomes = V2TendWrite.sweepOwnUsing f.Incident (V2TendNoEffect.settledWith f.Policy) f.Context
    Assert.Empty held
    Assert.Equal("root-settled-no-effect", outcomes.Head.Result)
    Assert.Equal(calls, f.Board.Calls.Length)
    Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)

[<Fact>]
let missing_or_tampered_proof_fences_without_rewriting_even_an_observed_target () =
    use f = new Fixture()
    let certificate = f.Admit() |> Result.defaultWith failwith
    let folder = Path.GetDirectoryName certificate
    f.Board.Items.["PVTI_fixture"].Values <- Map.ofList [ "Status", "In review" ]
    for name in [ "request.json"; "schema.json"; "readback.json"; "original.dll"; "certificate.json" ] do
        let path = Path.Combine(folder, name)
        let saved = File.ReadAllBytes path
        File.WriteAllText(path, "tampered")
        Assert.False(V2TendNoEffect.settledWith f.Policy f.Custody.Root f.Path)
        let held, _ = V2TendWrite.sweepOwnUsing f.Incident (V2TendNoEffect.settledWith f.Policy) f.Context
        Assert.Contains("PVTI_fixture", held)
        Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)
        File.WriteAllBytes(path, saved)
    File.Delete certificate
    let held, _ = V2TendWrite.sweepOwnUsing f.Incident (V2TendNoEffect.settledWith f.Policy) f.Context
    Assert.Contains("PVTI_fixture", held)
    Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)

[<Fact>]
let writer_exclusion_prevents_every_deciding_read_and_certificate () =
    use f = new Fixture()
    use lease = V2WriterLock.acquire f.Location
    Assert.True(f.Admit() |> Result.isError)
    Assert.Equal(0, f.SchemaCalls)
    Assert.Empty f.Board.Calls
    Assert.Empty f.Board.Mutations

[<Fact>]
let admission_refuses_changed_native_identity_preimage_and_wrong_provider_scalar () =
    for wrong in [ "preimage"; "identity"; "scalar" ] do
        use f = new Fixture()
        match wrong with
        | "preimage" -> f.Board.Items.["PVTI_fixture"].Values <- Map.ofList [ "Status", "Ready" ]
        | "identity" -> f.Board.AddItem("PVTI_fixture", { f.Board.Items.["PVTI_fixture"] with Ref = "FS-GG/fixture#2" })
        | _ -> f.Schema <- f.Schema.Replace("\"String\"", "\"ID\"")
        Assert.True(f.Admit() |> Result.isError)
        Assert.False(V2TendNoEffect.settledWith f.Policy f.Custody.Root f.Path)
        Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)
        Assert.Empty f.Board.Mutations

[<Fact>]
let production_policy_refuses_foreign_records_and_bad_artifacts_or_reconstruction () =
    use f = new Fixture()
    Assert.True(V2TendNoEffect.admitWith V2TendNoEffect.policy f.Dependencies f.Input |> Result.isError)
    Assert.Equal(0, f.SchemaCalls)
    let wrong = { f.Dependencies with Reconstruct = fun _ -> [| 0uy |] }
    Assert.True(V2TendNoEffect.admitWith f.Policy wrong f.Input |> Result.isError)
    File.WriteAllText(f.Input.RequestPath, "tampered")
    Assert.True(f.Admit() |> Result.isError)
    Assert.Empty f.Board.Mutations

[<Fact>]
let partial_certificate_write_never_settles_or_allows_replacement () =
    use f = new Fixture()
    let failed = { f.Dependencies with SyncDirectory = fun path -> if Path.GetFileName path = V2TendNoEffect.digest f.OriginalBytes then raise (IOException "fixture fsync") }
    Assert.True(V2TendNoEffect.admitWith f.Policy failed f.Input |> Result.isError)
    Assert.False(V2TendNoEffect.settledWith f.Policy f.Custody.Root f.Path)
    Assert.True(f.Admit() |> Result.isError)
    Assert.Equal<byte>(f.OriginalBytes, File.ReadAllBytes f.Path)
