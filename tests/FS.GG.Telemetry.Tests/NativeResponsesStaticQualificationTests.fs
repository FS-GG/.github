namespace FS.GG.Telemetry.Tests

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Nodes
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Telemetry
open FS.GG.Telemetry.Host

/// Declaration-only pre-install scenarios. No collector, credential read or provider call.
module NativeResponsesStaticQualificationTests =
    let private sha (bytes: byte array) = Convert.ToHexStringLower(SHA256.HashData bytes)
    let private loaded () =
        [ "host",typeof<HostConfig>.Assembly
          "client",typeof<DirectResponses.Phase>.Assembly
          "core",typeof<NativeResponses.Request>.Assembly
          "store",typeof<TelemetryStoreApplication.NativeCollectorDispatch>.Assembly ]
    let private require condition reason = if not condition then invalidOp reason
    let private rolePaths () = loaded() |> List.map (fun (role,assembly) -> role,assembly.Location) |> Map.ofList
    let private verifyClosure (profile: JsonElement) =
        let roots = profile.GetProperty("installedRoots").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
        require (roots.Length>0 && roots.Length<=4 && Array.distinct roots = roots) "static-roots"
        let below (root: string) (path: string) = path.StartsWith(root + string Path.DirectorySeparatorChar,StringComparison.Ordinal)
        for root in roots do
            require (Path.IsPathFullyQualified root && Path.GetFullPath root=root && Directory.Exists root && isNull(DirectoryInfo(root).LinkTarget)) "static-root-custody"
            for other in roots do require (root=other || not(below root other)) "static-overlapping-roots"
        let actual = HashSet<string>(StringComparer.Ordinal)
        let pending = Stack<string>(roots)
        let mutable entries = 0
        let mutable directories = 0
        while pending.Count>0 do
            let directory = pending.Pop()
            directories <- directories+1
            require (directories<=1024) "static-directory-bound"
            for path in Directory.EnumerateFileSystemEntries directory do
                entries <- entries+1
                require (entries<=4096) "static-entry-bound"
                let attributes = File.GetAttributes path
                require ((attributes &&& FileAttributes.ReparsePoint)=enum 0) "static-linked-entry"
                if (attributes &&& FileAttributes.Directory)<>enum 0 then pending.Push path
                else
                    require (actual.Add path && actual.Count<=512) "static-file-bound"
        let expected = HashSet<string>(StringComparer.Ordinal)
        let roles = Dictionary<string,string>(StringComparer.Ordinal)
        let names = loaded() |> List.map (fun (role,assembly) -> role,assembly.GetName().Name) |> Map.ofList
        let rows = profile.GetProperty("installedFiles")
        require (rows.ValueKind=JsonValueKind.Array && rows.GetArrayLength()>0 && rows.GetArrayLength()<=512) "static-row-bound"
        let mutable previous = ""
        let mutable total = 0L
        for row in rows.EnumerateArray() do
            let path = row.GetProperty("path").GetString()
            let bytes = row.GetProperty("bytes").GetInt64()
            require (Path.IsPathFullyQualified path && Path.GetFullPath path=path && StringComparer.Ordinal.Compare(previous,path)<0 && (roots |> Array.exists (fun root -> below root path))) "static-file-order"
            previous <- path
            require (bytes>0L && bytes<=200L*1024L*1024L && total<=200L*1024L*1024L-bytes && Configuration.responsesImmutableFile path) "static-file-custody"
            total <- total+bytes
            use stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)
            require (stream.Length=bytes && Convert.ToHexStringLower(SHA256.HashData stream)=row.GetProperty("sha256").GetString()) "static-file-changed"
            require (expected.Add path) "static-duplicate-file"
            for roleValue in row.GetProperty("components").EnumerateArray() do
                let role = roleValue.GetString()
                require (names.ContainsKey role && roles.TryAdd(role,path)) "static-component-role"
                require (Path.GetFileName path=names.[role]+".dll" && AssemblyName.GetAssemblyName(path).Name=names.[role]) "static-assembly-name"
        require (expected.SetEquals actual) "static-inventory-exact"
        require (roles.Count=4) "static-component-roster"
        for role,path in rolePaths() |> Map.toList do require (roles.ContainsKey role && roles.[role]=path) "static-loaded-location"

    let private context () =
        match Environment.GetEnvironmentVariable "FSGG_RESPONSES_STATIC_CONTEXT" with
        | null | "" -> None
        | path ->
            let bytes = Configuration.readResponsesPrivateBytes path path 65536 |> Result.defaultWith (String.concat ";" >> failwith)
            use document = JsonDocument.Parse(ReadOnlyMemory<byte> bytes)
            let root = document.RootElement
            let expected = set ["schema";"attemptId";"profilePath"]
            require (root.ValueKind=JsonValueKind.Object && (root.EnumerateObject() |> Seq.map _.Name |> Seq.toList |> List.sort)=Set.toList expected) "static-context-shape"
            require (root.GetProperty("schema").GetString()="fsgg.telemetry.responses-static-context/1") "static-context-schema"
            let profilePath = root.GetProperty("profilePath").GetString()
            let profileBytes = Configuration.readResponsesPrivateBytes path profilePath 65536 |> Result.defaultWith (String.concat ";" >> failwith)
            Some(path,root.GetProperty("attemptId").GetString(),profileBytes)

    let private requireMetadataBound (bytes: byte array) =
        require (bytes.Length<=131072) "static-evidence-bound"

    let private metadataBytes (attempt: string) (profileBytes: byte array) (scenario: string) (checks: string list) =
        use document = JsonDocument.Parse(ReadOnlyMemory<byte> profileBytes)
        let files = JsonSerializer.SerializeToUtf8Bytes<JsonElement>(document.RootElement.GetProperty "installedFiles")
        let node = JsonObject()
        node.["schema"] <- JsonValue.Create "fsgg.telemetry.responses-static-metadata-evidence/1"
        node.["attemptId"] <- JsonValue.Create attempt
        node.["scenario"] <- JsonValue.Create scenario
        node.["profileSha256"] <- JsonValue.Create(sha profileBytes)
        node.["installedFilesSha256"] <- JsonValue.Create(sha files)
        node.["managedInstalledFilesBase64"] <- JsonValue.Create(Convert.ToBase64String files)
        let components = JsonObject()
        for role,value in rolePaths() |> Map.toList do components.[role] <- JsonValue.Create value
        node.["loadedComponents"] <- components
        let completed = JsonObject()
        for name in checks do completed.[name] <- JsonValue.Create true
        node.["checks"] <- completed
        let bytes = JsonSerializer.SerializeToUtf8Bytes<JsonObject> node
        requireMetadataBound bytes
        bytes

    let private evidence (path: string) (attempt: string) (profileBytes: byte array) (scenario: string) (checks: string list) =
        let bytes = metadataBytes attempt profileBytes scenario checks
        let output = Path.Combine(Path.GetDirectoryName path,scenario+".json")
        let options = FileStreamOptions()
        options.Mode <- FileMode.CreateNew
        options.Access <- FileAccess.Write
        options.Share <- FileShare.None
        options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        use stream = new FileStream(output,options)
        stream.Write(bytes,0,bytes.Length)
        stream.Flush true

    [<Fact>]
    let ``managed profile and installed files use the exact same serializer bytes`` () =
        // HTML/nonASCII paths expose Python/default managed encoder differences.
        let files = JsonNode.Parse("""[{"path":"/A&BÅ/FS.GG.Telemetry.Host.dll","bytes":1,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","components":["host"]}]""")
        let profile = JsonObject()
        profile.["installedFiles"] <- files.DeepClone()
        let raw = JsonSerializer.SerializeToUtf8Bytes<JsonObject> profile
        use document = JsonDocument.Parse(ReadOnlyMemory<byte> raw)
        let viaElement = JsonSerializer.SerializeToUtf8Bytes<JsonElement>(document.RootElement.GetProperty "installedFiles")
        let direct = JsonSerializer.SerializeToUtf8Bytes<JsonNode> files
        Assert.Equal<byte>(direct,viaElement)
        Assert.Equal(sha direct,sha viaElement)

        // A full inventory expands when embedded as base64. Exercise the actual
        // metadata serializer without claiming physical/canonical admission.
        let inventory = JsonArray()
        for index in 0..284 do
            let row = JsonObject()
            row.["path"] <- JsonValue.Create(sprintf "/installed/static-qualification/prospective-product/h/dependency-%03d-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.dll" index)
            row.["bytes"] <- JsonValue.Create 1L
            row.["sha256"] <- JsonValue.Create(String('a',64))
            row.["components"] <- JsonArray()
            inventory.Add row
        let fullProfile = JsonObject()
        fullProfile.["installedFiles"] <- inventory
        let fullBytes = JsonSerializer.SerializeToUtf8Bytes<JsonObject> fullProfile
        use fullDocument = JsonDocument.Parse(ReadOnlyMemory<byte> fullBytes)
        let fullFiles = JsonSerializer.SerializeToUtf8Bytes<JsonElement>(fullDocument.RootElement.GetProperty "installedFiles")
        let directFullFiles = JsonSerializer.SerializeToUtf8Bytes<JsonArray> inventory
        Assert.Equal<byte>(directFullFiles,fullFiles)
        Assert.Equal(285,fullDocument.RootElement.GetProperty("installedFiles").GetArrayLength())
        let metadata = metadataBytes "synthetic-static-boundary" fullBytes "current-installed-closure"
                            ["physicalInventoryExact";"immutableFilesVerified";"assemblyNamesVerified";"loadedLocationsVerified"]
        Assert.True(metadata.Length>65536 && metadata.Length<=131072)
        use metadataDocument = JsonDocument.Parse(ReadOnlyMemory<byte> metadata)
        let roundtrip = Convert.FromBase64String(metadataDocument.RootElement.GetProperty("managedInstalledFilesBase64").GetString())
        Assert.Equal<byte>(fullFiles,roundtrip)
        Assert.Equal(sha fullFiles,metadataDocument.RootElement.GetProperty("installedFilesSha256").GetString())
        let atBound = Array.create<byte> 131072 32uy
        Array.Copy(metadata,atBound,metadata.Length)
        requireMetadataBound atBound
        Assert.Throws<InvalidOperationException>(fun () -> requireMetadataBound (Array.append atBound [|32uy|])) |> ignore

    [<Fact>]
    let ``installed static qualification binds the actual loaded product closure`` () =
        match context() with
        | None ->
            // Ordinary suite proves refusal only; without context no scenario evidence
            // is emitted and the producer cannot claim positive static qualification.
            use absent = JsonDocument.Parse("{}")
            Assert.ThrowsAny<Exception>(fun () -> verifyClosure absent.RootElement) |> ignore
        | Some(path,attempt,bytes) ->
            use document = JsonDocument.Parse(ReadOnlyMemory<byte> bytes)
            verifyClosure document.RootElement
            evidence path attempt bytes "current-installed-closure"
                ["physicalInventoryExact";"immutableFilesVerified";"assemblyNamesVerified";"loadedLocationsVerified"]

    [<Fact>]
    let ``credential roles are checked without reading secret contents`` () =
        let credential: CredentialConfig =
            { Reference="ingestion";SecretFile="/synthetic/ingestion";WorkspaceId="workspace";ProducerId="collector";StreamId="stream"
              Role="native-collector";GrantId="grant";GrantGeneration=1L;Revoked=false }
        let host: HostConfig =
            { Schema="fsgg.telemetry.host-config/2";ListenUrl="";CertificatePath="";CertificatePasswordFile="";ServiceLockPath=""
              Stores=[||];Credentials=[|credential|];BrowserPrincipals=[||];BrowserSession=Unchecked.defaultof<BrowserSessionConfig> }
        let check reference file = Configuration.validateResponsesCredentialRoles host "ingestion" reference file
        Assert.Empty(check "provider" "/synthetic/provider")
        Assert.NotEmpty(check "ingestion" "/synthetic/provider")
        Assert.NotEmpty(check "provider" "/synthetic/ingestion")
        Assert.NotEmpty(Configuration.validateResponsesCredentialRoles {host with Credentials=[|{credential with Role="generic"}|]} "ingestion" "provider" "/synthetic/provider")
        for invalid in [{credential with Revoked=true};{credential with GrantGeneration=0L}] do
            Assert.NotEmpty(Configuration.validateResponsesCredentialRoles {host with Credentials=[|invalid|]} "ingestion" "provider" "/synthetic/provider")
        Assert.NotEmpty(Configuration.validateResponsesCredentialRoles {host with Credentials=[|credential;credential|]} "ingestion" "provider" "/synthetic/provider")
        Assert.NotEmpty(check "" "/synthetic/provider")
        match context() with
        | None -> ()
        | Some(path,attempt,bytes) ->
            // Static role grammar test, not an operational credential enrollment.
            evidence path attempt bytes "credential-role-separation"
                ["distinctReferences";"providerNotIngestionFile";"positiveRoles";"aliasRefused"]
