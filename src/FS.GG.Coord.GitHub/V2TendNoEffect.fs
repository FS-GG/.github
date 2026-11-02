namespace FS.GG.Coord.GitHub

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json.Nodes
open System.Text.RegularExpressions

module V2TendNoEffect =
    type Input =
        { CustodyRoot: string; OriginalPath: string; RequestPath: string
          OriginalRuntimeDirectory: string; RootAdmissionReference: string }

    type internal Policy =
        { Run: string; Attempts: Map<string, string>; Assemblies: Map<string, string> }

    type internal Admission =
        { Acquire: unit -> IDisposable
          ReadCurrent: JsonObject -> Result<string, string>
          ReadSchema: unit -> Result<string, string>
          Reconstruct: JsonObject -> byte array
          SyncDirectory: string -> unit
          Clock: TimeProvider }

    // Hashes only: no native card identities, private paths, credentials or provider bodies.
    // Independently checked against the exact installed renderer/serializer, not a submitted invalid mutation.
    let internal policy =
        { Run = "20261102T151514Z-a8647ab2"
          Attempts = Map.ofList [
            "8b34745184192a2b19f514a5094e71958f3ab12aa559bbeb3ecbb0d4d4dea1be", "ec8943ab2051b67fda77e63fa3d126ddd3852fb6bc96c6a24e69f4906ea08321"
            "b58a289c8371b5b4b017fdc95879ada31fd52244995fd810732d19855000aef6", "0b4e095820f6a8f2e520582f88bd9196bc253750d790a46e0f5d3bff0a0af976"
            "dd0f01e00075f0cb253123a8e7954611564761c7c424b6d3f4446d13f3a42c29", "dd009cf06175cc0e573070a1d5c5755045c836ccf38b3b8825d0e77297e54f05" ]
          Assemblies = Map.ofList [
            "FS.GG.Coord.GitHub.dll", "b919d9cab57ba576f7c74ff3a8fa8c14c7b211d8c9c5287a11ed74e29afb6e7a"
            "FS.GG.Coord.Core.dll", "2085393d3dd625ecc2aef0c5e0fb7a477cffb7f19589bbad4470bf33ce2ee9e3"
            "fsgg-coord-engine.dll", "6afa1c615234b59a70cf140e1079af1d386621181803033349944138b0800cae" ] }

    let private project = "PVT_kwDOEYAWY84Bldpa"
    let internal digest (bytes: byte array) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
    let private read (path: string) =
        let info = FileInfo path
        if info.LinkTarget <> null || info.Length > 16L * 1024L * 1024L then invalidOp "invalid evidence file"
        File.ReadAllBytes path
    let private json (bytes: byte array) = JsonNode.Parse(Text.Encoding.UTF8.GetString bytes).AsObject()
    let private text (node: JsonNode) (name: string) = node[name].GetValue<string>()
    let private privateFile (path: string) =
        if not (OperatingSystem.IsWindows()) && File.GetUnixFileMode path <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then invalidOp "private evidence required"
        read path
    let private directory (path: string) =
        if DirectoryInfo(path).LinkTarget <> null then invalidOp "symlink directory"
        Directory.CreateDirectory(path, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        if File.GetUnixFileMode path <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) then invalidOp "private directory required"
    let private append (sync: string -> unit) (path: string) (bytes: byte array) =
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                                       Options = FileOptions.WriteThrough, UnixCreateMode = Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
        use stream = new FileStream(path, options)
        stream.Write(bytes, 0, bytes.Length)
        stream.Flush true
        sync (Path.GetDirectoryName path)
    let private bytes (value: JsonNode) = Text.Encoding.UTF8.GetBytes(value.ToJsonString())
    let private schemaValid (value: JsonObject) =
        try
            let root = value["data"]["__type"]
            root["kind"].GetValue<string>() = "INPUT_OBJECT"
            && root["name"].GetValue<string>() = "ProjectV2FieldValue"
            && (root["inputFields"].AsArray() |> Seq.exists (fun f ->
                text f "name" = "singleSelectOptionId" && text (f["type"]) "kind" = "SCALAR" && text (f["type"]) "name" = "String"))
            && value["errors"] = null
        with _ -> false
    let private expectedPath (root: string) (p: Policy) (original: string) =
        Path.GetDirectoryName(Path.GetFullPath original) = Path.Combine(Path.GetFullPath root, p.Run)
    let private proofDirectory (root: string) (sha: string) = Path.Combine(root, "no-effect-settlements", sha)
    let internal isIncident (path: string) = Path.GetFileName(Path.GetDirectoryName path) = policy.Run

    let private readbackValid (original: JsonObject) (current: JsonObject) =
        try
            text current "projectId" = project && text current "itemId" = text original "itemId"
            && text current "ref" = text original "ref" && not (String.IsNullOrWhiteSpace(text current "contentId"))
            && current["archived"].GetValue<bool>() = false
            && current["fields"].ToJsonString() = original["fields"].ToJsonString()
            && (original["fields"].AsArray() |> Seq.forall (fun field ->
                let value = current["fieldValues"][text field "fieldId"]
                text value "__typename" = "ProjectV2ItemFieldSingleSelectValue"
                && text (value["field"]) "id" = text field "fieldId"
                && text value "name" = text field "preimage" && not (String.IsNullOrWhiteSpace(text value "optionId"))))
        with _ -> false

    let internal settledWith (p: Policy) root path =
        try
            let originalBytes = privateFile path
            let originalSha = digest originalBytes
            match p.Attempts.TryFind originalSha with
            | None -> false
            | Some requestSha ->
                let original = json originalBytes
                let folder = proofDirectory root originalSha
                let certificate = json (privateFile (Path.Combine(folder, "certificate.json")))
                let expectedAssets = set ([ "original.json"; "request.json"; "schema.json"; "readback.json" ] @ (p.Assemblies |> Map.keys |> List.ofSeq))
                let assets = certificate["assets"].AsObject()
                expectedPath root p path && text original "run" = p.Run && text original "kind" = "fields"
                && text original "state" = "possible-send"
                && text certificate "schema" = "fsgg.tend-no-effect-certificate/1"
                && text certificate "incidentRun" = p.Run && text certificate "originalSha256" = originalSha
                && text certificate "disposition" = "not-executed-invalid-option-scalar"
                && Regex.IsMatch(text certificate "rootAdmissionReference", @"^https://github\.com/FS-GG/\.github/issues/5206#issuecomment-\d+$")
                && (assets |> Seq.map _.Key |> Set.ofSeq) = expectedAssets
                && (assets |> Seq.forall (fun property -> digest (privateFile (Path.Combine(folder, property.Key))) = property.Value.GetValue<string>()))
                && text assets "original.json" = originalSha && text assets "request.json" = requestSha
                && (p.Assemblies |> Map.forall (fun name sha -> text assets name = sha))
                && schemaValid (json (privateFile (Path.Combine(folder, "schema.json"))))
                && readbackValid original (json (privateFile (Path.Combine(folder, "readback.json"))))
        with _ -> false

    let internal settled root path = settledWith policy root path

    let internal admitWith (p: Policy) (deps: Admission) (input: Input) =
        try
            // All deciding provider reads occur inside the actual writer exclusion selected by the controller.
            use lease = deps.Acquire()
            let originalBytes = privateFile input.OriginalPath
            let originalSha = digest originalBytes
            let requestSha = p.Attempts.TryFind originalSha |> Option.defaultWith (fun () -> invalidOp "not an admitted incident attempt")
            if not (expectedPath input.CustodyRoot p input.OriginalPath) then invalidOp "wrong custody run"
            let original = json originalBytes
            if text original "schema" <> "fsgg.board-tend-custody/1" || text original "run" <> p.Run
               || text original "kind" <> "fields" || text original "state" <> "possible-send" then invalidOp "wrong original custody kind"
            if not (Regex.IsMatch(input.RootAdmissionReference, @"^https://github\.com/FS-GG/\.github/issues/5206#issuecomment-\d+$")) then invalidOp "Root admission reference required"
            let request = read input.RequestPath
            if digest request <> requestSha || digest (deps.Reconstruct original) <> requestSha then invalidOp "original request reconstruction differs"
            let assemblyBytes = p.Assemblies |> Map.map (fun name expected ->
                let value = read (Path.Combine(input.OriginalRuntimeDirectory, name))
                if digest value <> expected then invalidOp "original runtime identity differs"
                value)
            let providerSchema = deps.ReadSchema() |> Result.defaultWith invalidOp
            if not (schemaValid (JsonNode.Parse(providerSchema).AsObject())) then invalidOp "provider input scalar is not String"
            let current = deps.ReadCurrent original |> Result.defaultWith invalidOp
            if not (readbackValid original (JsonNode.Parse(current).AsObject())) then invalidOp "current identity or preimage differs"
            let parent = Path.Combine(input.CustodyRoot, "no-effect-settlements")
            directory parent
            deps.SyncDirectory input.CustodyRoot
            let folder = proofDirectory input.CustodyRoot originalSha
            if Directory.Exists folder || File.Exists folder then invalidOp "existing settlement evidence; no replacement"
            directory folder
            deps.SyncDirectory parent
            let artifacts = Map.ofList [ "original.json", originalBytes; "request.json", request
                                         "schema.json", Text.Encoding.UTF8.GetBytes providerSchema
                                         "readback.json", Text.Encoding.UTF8.GetBytes current ]
                            |> Map.fold (fun (values: Map<string, byte array>) key value -> values.Add(key, value)) assemblyBytes
            let assets = JsonObject()
            for KeyValue(name, value) in artifacts do
                append deps.SyncDirectory (Path.Combine(folder, name)) value
                assets[name] <- digest value
            let certificate = JsonObject()
            certificate["schema"] <- "fsgg.tend-no-effect-certificate/1"
            certificate["incidentRun"] <- p.Run
            certificate["originalSha256"] <- originalSha
            certificate["disposition"] <- "not-executed-invalid-option-scalar"
            certificate["rootAdmissionReference"] <- input.RootAdmissionReference
            certificate["admittedAt"] <- deps.Clock.GetUtcNow().ToString("o")
            certificate["originalResponse"] <- "Unknown; no provider body or wire capture retained"
            certificate["assets"] <- assets
            let destination = Path.Combine(folder, "certificate.json")
            append deps.SyncDirectory destination (bytes certificate)
            Ok destination
        with error ->
            let detail = error.Message.Substring(0, min 2048 error.Message.Length)
            Error("no-effect settlement refused; preserve originals: " + detail)
