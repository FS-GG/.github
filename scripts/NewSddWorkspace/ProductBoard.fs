namespace NewSddWorkspace

/// Product integration consumes the adapter's closed Binding wire. Preparation is read-only;
/// every missing producer file or conflicting owner edit refuses before installation.
module ProductBoard =
    open System
    open System.IO
    open System.Text.Json.Nodes
    open System.Security.Cryptography
    open FS.GG.Coord.GitHub.V2Projection
    open FS.GG.Coord.Cli

    type FileChange = { Path: string; Before: byte array option; After: byte array; Executable: bool; BeforeMode: UnixFileMode option }
    type Plan = { Repository: string; ProjectId: string; Changes: FileChange list }

    let private bytes (value: string) = Text.Encoding.UTF8.GetBytes value
    let private digest (value: byte array) = SHA256.HashData value |> Convert.ToHexString |> _.ToLowerInvariant()
    let private immutableRevision (value: string) =
        value.Length = 40 && (value |> Seq.forall Uri.IsHexDigit)
    let private jsonObject (path: string) =
        if File.Exists path then
            match JsonNode.Parse(File.ReadAllText path) with
            | :? JsonObject as root -> root
            | _ -> invalidOp ("expected JSON object: " + path)
        else JsonObject()
    let private objectAt (root: JsonObject) (name: string) =
        match root.[name] with
        | null -> let value = JsonObject() in root.[name] <- value; value
        | :? JsonObject as value -> value
        | _ -> invalidOp ("expected object: " + name)
    let private setOwned (root: JsonObject) (key: string) (value: string) =
        match root.[key] with
        | null -> root.[key] <- JsonValue.Create(value: string)
        | node when node.GetValue<string>() = value -> ()
        | _ -> invalidOp ("owner-authored configuration conflicts: " + key)
    let private encode (root: JsonObject) = root.ToJsonString(System.Text.Json.JsonSerializerOptions(WriteIndented = true)) |> bytes

    /// Pure selected repository/adapter validation, using the canonical parser rather than a second schema.
    let validate (repository: string) (adapterSha256: string) (bindingJson: string) =
        BoardV2Application.parseBinding bindingJson
        |> Result.bind (fun binding ->
            if binding.BindingVersion <> 3 then Error "product integration requires Binding version 3"
            elif binding.Repositories <> Set.singleton repository then Error "binding does not select this exact repository"
            elif binding.ArtifactSha256 <> adapterSha256 then Error "binding differs from the loaded adapter assembly SHA-256"
            else Ok binding)

    /// Retained receivers must agree with their actual credential-free canonical GitHub origin.
    let validateRemote (repository: string) (origin: string) =
        let path =
            if origin.StartsWith("git@github.com:", StringComparison.Ordinal) then Some(origin.Substring(15))
            else
                match Uri.TryCreate(origin, UriKind.Absolute) with
                | true, uri when uri.Scheme = "https" && uri.Host = "github.com" && uri.UserInfo = "" && uri.Query = "" && uri.Fragment = "" && uri.IsDefaultPort -> Some(uri.AbsolutePath.TrimStart '/')
                | _ -> None
        match path with
        | Some value when (if value.EndsWith(".git", StringComparison.Ordinal) then value.Substring(0, value.Length - 4) else value) = repository -> Ok()
        | _ -> Error "workspace origin does not match the exact selected GitHub repository"

    /// Authority is captured before scaffold effects, never inferred from provider-created Git.
    type OriginAuthority = RetainedGit | FreshTarget

    /// Opaque invocation authority: an existing directory without Git is still owner-authored.
    type FreshScaffoldTarget = private FreshScaffoldTarget of string
    type GeneratedToolManifest = private GeneratedToolManifest of string * byte array * UnixFileMode option

    let captureFreshScaffoldTarget target =
        let absent =
            try File.GetAttributes target |> ignore; false
            with
            | :? FileNotFoundException | :? DirectoryNotFoundException -> true
            | _ -> false
        if absent then Some(FreshScaffoldTarget(Path.GetFullPath target)) else None

    let private generatedManifestPreimage target =
        let path = Path.Combine(target, ".config", "dotnet-tools.json")
        for entry in [ target; Path.Combine(target, ".config"); path ] do
            if (File.GetAttributes entry &&& FileAttributes.ReparsePoint) <> enum<FileAttributes> 0 then
                invalidOp "generated tool manifest path must not contain symbolic links"
        let before = File.ReadAllBytes path
        let mode = if OperatingSystem.IsWindows() then None else Some(File.GetUnixFileMode path)
        before, mode

    /// Capture immediately after the successful SDD call, before any subsequent creator phase.
    let captureGeneratedToolManifest (FreshScaffoldTarget originalTarget) target scaffoldSucceeded =
        try
            if not scaffoldSucceeded || Path.GetFullPath target <> originalTarget then
                invalidOp "generated tool authority requires this successful fresh scaffold"
            let provenance = jsonObject (Path.Combine(target, ".fsgg", "scaffold-provenance.json"))
            if provenance.["schemaVersion"].GetValue<int>() <> 1
               || provenance.["generator"].["id"].GetValue<string>() <> "FS.GG.SDD.Artifacts" then
                invalidOp "generated tool manifest lacks SDD scaffold provenance"
            let owned = provenance.["sddOwnedPaths"].AsArray()
            let owners = owned |> Seq.filter (fun row -> row.["path"].GetValue<string>() = ".config/dotnet-tools.json") |> Seq.toList
            if owners.Length <> 1 || owners.Head.["owner"].GetValue<string>() <> "sdd" then
                invalidOp "generated tool manifest is not uniquely SDD-owned"
            let before, mode = generatedManifestPreimage target
            let root = JsonNode.Parse(Text.Encoding.UTF8.GetString before).AsObject()
            if root.["version"].GetValue<int>() <> 1 || not (root.["isRoot"].GetValue<bool>()) then
                invalidOp "generated tool manifest must be a root manifest"
            let tool = root.["tools"].["fs.gg.coord.cli"].AsObject()
            if String.IsNullOrWhiteSpace(tool.["version"].GetValue<string>()) || tool.["commands"].AsArray().Count = 0 then
                invalidOp "generated coordination default is malformed"
            Ok(GeneratedToolManifest(originalTarget, before, mode))
        with error -> Error("generated tool manifest capture refused: " + error.Message)

    let captureOriginAuthority target =
        if Directory.Exists(Path.Combine(target, ".git")) || File.Exists(Path.Combine(target, ".git")) then RetainedGit
        else FreshTarget

    /// A fresh successful scaffold may initialize an unborn repository without choosing an origin.
    /// Existing repositories and retrofit never receive this allowance; any present origin is exact.
    let validateOrigin authority scaffoldSucceeded target repository (runGit: string list -> int * string) =
        let hasGit = captureOriginAuthority target = RetainedGit
        if not hasGit then
            if authority = RetainedGit then Error "retained workspace Git metadata disappeared"
            else Ok()
        else
            let code, origin = runGit [ "remote"; "get-url"; "origin" ]
            if code = 0 then validateRemote repository (origin.Trim())
            elif authority = FreshTarget && scaffoldSucceeded && Directory.Exists(Path.Combine(target, ".git")) then
                let rootCode, root = runGit [ "rev-parse"; "--show-toplevel" ]
                let configCode, configured = runGit [ "config"; "--local"; "--get"; "remote.origin.url" ]
                let branchCode, branch = runGit [ "symbolic-ref"; "--quiet"; "HEAD" ]
                let headCode, _ = runGit [ "rev-parse"; "--verify"; "HEAD" ]
                if rootCode = 0 && Path.GetFullPath(root.Trim()) = Path.GetFullPath target
                   && configCode = 1 && String.IsNullOrWhiteSpace configured
                   && branchCode = 0 && branch.Trim().StartsWith("refs/heads/", StringComparison.Ordinal)
                   && headCode = 128 then Ok()
                else Error "fresh scaffold Git is not a readable originless unborn repository"
            else Error "retained workspace GitHub origin is unreadable"

    /// Stage reviewed producer bytes and preserving JSON merges. Fetch is injected for offline controls.
    /// The immutable kit revision identifies both manifest projections, shim and exact local tool pin.
    let private prepareWithGeneratedManifest generatedManifest (target: string) (repository: string) (kitRevision: string) (bindingJson: string) (fetch: string -> Result<string, string>) =
        try
            match generatedManifest with
            | Some(GeneratedToolManifest(originalTarget, before, beforeMode)) ->
                let actual, mode = generatedManifestPreimage target
                if Path.GetFullPath target <> originalTarget || actual <> before || mode <> beforeMode then
                    invalidOp "generated tool manifest changed after the successful scaffold"
            | None -> ()
            if not (immutableRevision kitRevision) then invalidOp "--board-kit-ref requires an immutable 40 hexadecimal revision"
            let adapterBytes = File.ReadAllBytes typeof<Binding>.Assembly.Location
            let binding = validate repository (digest adapterBytes) bindingJson |> function Ok value -> value | Error error -> invalidOp error
            let source path = fetch path |> function Ok value -> value | Error error -> invalidOp error
            let staged = ResizeArray<string * byte array * bool * bool>()
            let safeRelative (value: string) =
                not (String.IsNullOrWhiteSpace value) && not (Path.IsPathRooted value)
                && not (value.Contains '\\') && (value.Split('/') |> Array.forall (fun part -> part <> ".." && part <> "." && part <> ""))
            let stageSkills manifestPath selected =
                let manifest = JsonNode.Parse(source manifestPath).AsObject()
                if manifest.["schemaVersion"].GetValue<int>() <> 2 then invalidOp "unsupported skill directory manifest"
                let found = ResizeArray<string>()
                for row in manifest.["skills"].AsArray() do
                    let id = row.["id"].GetValue<string>()
                    if selected |> Option.forall (fun ids -> Set.contains id ids) then
                        if not (safeRelative id) || id.Contains '/' then invalidOp "unsafe skill id"
                        found.Add id
                        for file in row.["files"].AsArray() do
                            let rel = file.["path"].GetValue<string>()
                            if not (safeRelative rel) then invalidOp "unsafe skill manifest path"
                            let content = source (sprintf ".claude/skills/%s/%s" id rel) |> bytes
                            if digest content <> file.["sha256"].GetValue<string>() then invalidOp ("skill digest mismatch: " + id + "/" + rel)
                            for root in [ ".claude/skills"; ".agents/skills" ] do
                                staged.Add(sprintf "%s/%s/%s" root id rel, content, file.["executable"].GetValue<bool>(), false)
                match selected with
                | Some ids when Set.ofSeq found <> ids -> invalidOp "published product consumer payload is incomplete"
                | _ -> ()
            stageSkills "registry/coordination-kit-skill-manifest.json" (Some(Set.ofList [ "check-board"; "cross-repo-coordination"; "initialize-sdd-workspace"; "intra-repo-parallel-work"; "pnext-item" ]))
            stageSkills "registry/driver-skill-manifest.json" (Some(Set.ofList [ "work-board"; "work-board-normal"; "work-board-best"; "padd-item" ]))
            staged.Add("scripts/fsgg-coord", source "scripts/fsgg-coord" |> bytes, true, false)
            let manifest = JsonNode.Parse(source "dist/dotnet/.config/dotnet-tools.json").AsObject()
            let selectedTool = manifest.["tools"].["fs.gg.coord.cli"].AsObject()
            let version = selectedTool.["version"].GetValue<string>()
            let actualVersion =
                Reflection.Assembly.Load("fsgg-coord-engine").GetCustomAttributes(typeof<Reflection.AssemblyInformationalVersionAttribute>, false)
                |> Array.map (fun value -> (value :?> Reflection.AssemblyInformationalVersionAttribute).InformationalVersion.Split('+').[0])
                |> Array.tryExactlyOne
            if actualVersion <> Some version then invalidOp "published tool pin differs from the creator's coherent adapter version"
            let toolsRoot =
                match generatedManifest with
                | Some(GeneratedToolManifest(_, before, _)) -> JsonNode.Parse(Text.Encoding.UTF8.GetString before).AsObject()
                | None -> jsonObject (Path.Combine(target, ".config", "dotnet-tools.json"))
            match toolsRoot.["isRoot"] with
            | null -> toolsRoot.["isRoot"] <- JsonValue.Create true
            | node when node.GetValue<bool>() -> ()
            | _ -> invalidOp "non-root tool manifest conflicts with selected adapter"
            if isNull toolsRoot.["version"] then toolsRoot.["version"] <- JsonValue.Create 1
            let tools = objectAt toolsRoot "tools"
            match tools.["fs.gg.coord.cli"] with
            | null -> tools.["fs.gg.coord.cli"] <- selectedTool.DeepClone()
            | node when JsonNode.DeepEquals(node, selectedTool) -> ()
            | _ when generatedManifest.IsSome -> tools.["fs.gg.coord.cli"] <- selectedTool.DeepClone()
            | _ -> invalidOp "owner-authored adapter pin conflicts with selected publication"
            staged.Add(".config/dotnet-tools.json", encode toolsRoot, false, true)
            staged.Add(".fsgg/board-v2-binding.json", bytes bindingJson, false, false)
            let settings = jsonObject (Path.Combine(target, ".claude", "settings.json"))
            let env = objectAt settings "env"
            for legacy in [ "FSGG_COORD_OWNER"; "FSGG_COORD_PROJECT"; "FSGG_COORD_OWNER_TYPE"; "FSGG_COORD_CHORE_LOCKS" ] do
                if not (isNull env.[legacy]) then invalidOp ("legacy board environment requires explicit migration: " + legacy)
            setOwned env "FSGG_COORD_BOARD_MODE" "v2"
            setOwned env "FSGG_COORD_BOARD_BINDING" ".fsgg/board-v2-binding.json"
            setOwned env "FSGG_COORD_REPOSITORY" repository
            staged.Add(".claude/settings.json", encode settings, false, true)
            let provenance = jsonObject (Path.Combine(target, ".fsgg", "scaffold-provenance.json"))
            let selected = objectAt provenance "productBoard"
            for key, value in
                [ "repository", repository; "projectId", binding.ProjectId; "ownerKind", "organization"
                  "owner", binding.Owner; "bindingPath", ".fsgg/board-v2-binding.json"; "bindingSha256", digest (bytes bindingJson)
                  "adapterVersion", version; "adapterSha256", binding.ArtifactSha256; "kitRevision", kitRevision
                  "schemaVersion", string binding.SchemaVersion; "bindingVersion", string binding.BindingVersion
                  "recipeRevision", binding.RecipeRevision; "populationRevision", binding.PopulationRevision ] do
                setOwned selected key value
            staged.Add(".fsgg/scaffold-provenance.json", encode provenance, false, true)
            let duplicates = staged |> Seq.countBy (fun (path, _, _, _) -> path) |> Seq.exists (fun (_, count) -> count <> 1)
            if duplicates then invalidOp "duplicate producer destination"
            let changes =
                staged |> Seq.choose (fun (rel, after, executable, merge) ->
                    let path = Path.Combine(target, rel)
                    let before = if File.Exists path then Some(File.ReadAllBytes path) else None
                    let mode = if before.IsSome && not (OperatingSystem.IsWindows()) then Some(File.GetUnixFileMode path) else None
                    match generatedManifest with
                    | Some(GeneratedToolManifest(_, captured, capturedMode)) when rel = ".config/dotnet-tools.json" ->
                        generatedManifestPreimage target |> ignore
                        if before <> Some captured || mode <> capturedMode then
                            invalidOp "generated tool manifest changed during preparation"
                    | _ -> ()
                    if executable && (mode |> Option.exists (fun value -> (value &&& UnixFileMode.UserExecute) = enum<UnixFileMode> 0)) then
                        invalidOp ("selected executable has conflicting mode: " + rel)
                    match before with
                    | Some current when current = after -> None
                    | Some current when merge && JsonNode.DeepEquals(JsonNode.Parse(Text.Encoding.UTF8.GetString current), JsonNode.Parse(Text.Encoding.UTF8.GetString after)) -> None
                    | Some _ when not merge -> invalidOp ("owner-authored file conflicts: " + rel)
                    | _ -> Some { Path = rel; Before = before; After = after; Executable = executable; BeforeMode = mode }) |> Seq.toList
            Ok { Repository = repository; ProjectId = binding.ProjectId; Changes = changes }
        with error -> Error("product V2 preparation refused: " + error.Message)

    /// Existing targets and retrofit always preserve conflicting owner pins.
    let prepare target repository kitRevision bindingJson fetch =
        prepareWithGeneratedManifest None target repository kitRevision bindingJson fetch

    /// Only the opaque, same-invocation successful fresh scaffold snapshot grants replacement.
    let prepareGenerated snapshot target repository kitRevision bindingJson fetch =
        prepareWithGeneratedManifest (Some snapshot) target repository kitRevision bindingJson fetch

    /// Recheck every staged preimage immediately before the first write; restore original bytes on
    /// failure. This local installation never runs an adapter, accesses GitHub or changes a board.
    let apply target (plan: Plan) =
        let written = ResizeArray<FileChange>()
        try
            for change in plan.Changes do
                let path = Path.Combine(target, change.Path)
                let actual = if File.Exists path then Some(File.ReadAllBytes path) else None
                let mode = if actual.IsSome && not (OperatingSystem.IsWindows()) then Some(File.GetUnixFileMode path) else None
                if actual <> change.Before || mode <> change.BeforeMode then invalidOp ("workspace changed after preview: " + change.Path)
            for change in plan.Changes do
                let path = Path.Combine(target, change.Path)
                Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                written.Add change
                File.WriteAllBytes(path, change.After)
                if not (OperatingSystem.IsWindows()) then
                    File.SetUnixFileMode(path, change.BeforeMode |> Option.defaultValue (if change.Executable then enum<UnixFileMode> 493 else enum<UnixFileMode> 420))
            Ok()
        with error ->
            let failures = ResizeArray<string>()
            for change in Seq.rev written do
                try
                    let path = Path.Combine(target, change.Path)
                    match change.Before with
                    | Some value ->
                        File.WriteAllBytes(path, value)
                        change.BeforeMode |> Option.iter (fun mode -> File.SetUnixFileMode(path, mode))
                    | None -> File.Delete path
                with rollback -> failures.Add rollback.Message
            Error(error.Message + (if failures.Count = 0 then "" else "; rollback incomplete: " + String.concat "; " failures))
