namespace FSGG.Telemetry.PersistentV3

open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions

module Preparation =
    type private ResultBuilder() =
        member _.Bind(value, binder) = Result.bind binder value
        member _.Return(value) = Ok value
        member _.ReturnFrom(value) = value
        member _.Zero() = Ok()
        member _.Delay(action) = action
        member _.Run(action) = action()
        member _.Combine(value, next) = Result.bind (fun () -> next()) value
        member _.For(values: seq<'value>, body: 'value -> Result<unit, string>) =
            use enumerator = values.GetEnumerator()
            let rec loop () =
                if enumerator.MoveNext() then Result.bind loop (body enumerator.Current) else Ok()
            loop()

    let private result = ResultBuilder()

    [<Literal>]
    let CanonicalVerifierModuleSha256 = "8d6a33beae9a4de84fa7a703809e9b1a1656359a085f92091cf56de3b77fd3ba"

    let private expectedRecoveryCoverage =
        [| "configuration"; "credentials"; "evidence"; "manager-receipt"; "manager-sidecar"
           "quiesced-store"; "retained-native-source"; "source-references"; "verifier-runtime" |]

    let private expectedPrivateInputs =
        [| "credential-secret"; "evidence"; "host-configuration"; "persistent-store"; "retained-native-source" |]

    let private refuse message = Error("persistent-v3-preparation-refused: " + message)
    let private optionResult error value = match value with Some found -> Ok found | None -> Error error
    let private exactProperties (expected: string array) (value: JsonElement) =
        value.ValueKind = JsonValueKind.Object
        && (value.EnumerateObject() |> Seq.length) = expected.Length
        && (value.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq) = Set.ofArray expected

    let private sha256 (bytes: byte array) =
        Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

    let private string (name: string) (value: JsonElement) =
        let mutable property = Unchecked.defaultof<JsonElement>
        if value.TryGetProperty(name, &property) && property.ValueKind = JsonValueKind.String then
            let result = property.GetString()
            if String.IsNullOrWhiteSpace result then None else Some result
        else None

    let private boolean (name: string) (value: JsonElement) =
        let mutable property = Unchecked.defaultof<JsonElement>
        if value.TryGetProperty(name, &property)
           && (property.ValueKind = JsonValueKind.True || property.ValueKind = JsonValueKind.False) then Some(property.GetBoolean())
        else None

    let private child (name: string) (value: JsonElement) =
        let mutable property = Unchecked.defaultof<JsonElement>
        if value.TryGetProperty(name, &property) && property.ValueKind = JsonValueKind.Object then Some property else None

    let private array (name: string) (value: JsonElement) =
        let mutable property = Unchecked.defaultof<JsonElement>
        if value.TryGetProperty(name, &property) && property.ValueKind = JsonValueKind.Array then Some(property.EnumerateArray() |> Seq.toArray)
        else None

    let private sha64 (value: string) = Regex.IsMatch(value, "^[0-9a-f]{64}$") && value <> String('0', 64)
    let private sha40 (value: string) = Regex.IsMatch(value, "^[0-9a-f]{40}$") && value <> String('0', 40)
    let private identity (value: string) = Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
    let private absolutePath (value: string) =
        try Path.IsPathFullyQualified value && Path.GetFullPath(value) = value
        with _ -> false
    let private httpsUri (value: string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri -> uri.Scheme = Uri.UriSchemeHttps && not (String.IsNullOrWhiteSpace uri.Host)
        | _ -> false
    let private bounded (value: string) =
        value.Length <= 128 && value |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || ".:_-/@+".Contains c)

    let private requiredString (label: string) (name: string) (element: JsonElement) (predicate: string -> bool) =
        match string name element with
        | Some value when predicate value -> Ok value
        | _ -> refuse (label + "." + name)

    let private parseArtifact (label: string) (expected: string array) (value: JsonElement) =
        if not (exactProperties expected value) then refuse (label + " is not closed") else
        result {
            let! revision = requiredString label "sourceRevision" value sha40
            let! tree = requiredString label "sourceTree" value sha40
            let! uri = requiredString label "artifactUri" value httpsUri
            let! digest = requiredString label "artifactSha256" value sha64
            let! manifest = requiredString label "manifestSha256" value sha64
            return revision, tree, uri, digest, manifest
        }

    let private strings (label: string) (values: JsonElement array) =
        let parsed = values |> Array.map (fun value -> if value.ValueKind = JsonValueKind.String then value.GetString() else null)
        if parsed |> Array.exists String.IsNullOrWhiteSpace then refuse label else Ok parsed

    let prepare (bytes: byte array) =
        try
            use document = JsonDocument.Parse bytes
            let root = document.RootElement
            if root.ValueKind = JsonValueKind.Object && (root.EnumerateObject() |> Seq.isEmpty) then
                Error "persistent-v3-preparation-unavailable: required records absent"
            elif not (exactProperties [| "schema"; "identityClass"; "manager"; "host"; "installation"; "sourceGrant"; "sourceReference"; "verifierRuntime"; "recovery" |] root) then
                refuse "input is not closed"
            elif string "schema" root <> Some "fsgg.telemetry.persistent-v3-preparation-input/1" then refuse "schema"
            elif string "identityClass" root <> Some "public-artifact" && string "identityClass" root <> Some "synthetic-test" then refuse "identityClass"
            else
                result {
                    let identityClass = string "identityClass" root |> Option.get
                    let! manager = child "manager" root |> optionResult "persistent-v3-preparation-refused: manager"
                    let! managerRevision, managerTree, managerUri, managerDigest, managerManifest =
                        parseArtifact "manager" [| "sourceRevision"; "sourceTree"; "artifactUri"; "artifactSha256"; "manifestSha256" |] manager
                    let! host = child "host" root |> optionResult "persistent-v3-preparation-refused: host"
                    if not (exactProperties [| "sourceRevision"; "sourceTree"; "artifactUri"; "artifactSha256"; "manifestSha256"; "journalSha256" |] host) then
                        return! refuse "host is not closed"
                    let! hostRevision = requiredString "host" "sourceRevision" host sha40
                    let! hostTree = requiredString "host" "sourceTree" host sha40
                    let! hostUri = requiredString "host" "artifactUri" host httpsUri
                    let! hostDigest = requiredString "host" "artifactSha256" host sha64
                    let! hostManifest = requiredString "host" "manifestSha256" host sha64
                    let! hostJournal = requiredString "host" "journalSha256" host sha64
                    let! installation = child "installation" root |> optionResult "persistent-v3-preparation-refused: installation"
                    if not (exactProperties [| "schema"; "hostConfigPath"; "credentialReference"; "executablePath"; "executableSha256"; "codexHome"; "evidenceRoot"; "provider"; "model"; "effort" |] installation)
                       || string "schema" installation <> Some "fsgg.telemetry.native-collector-installation/3" then
                        return! refuse "installation is not closed v3"
                    let! hostConfig = requiredString "installation" "hostConfigPath" installation absolutePath
                    let! credential = requiredString "installation" "credentialReference" installation identity
                    let! executable = requiredString "installation" "executablePath" installation absolutePath
                    let! executableSha = requiredString "installation" "executableSha256" installation sha64
                    let! codexHome = requiredString "installation" "codexHome" installation absolutePath
                    let! evidenceRoot = requiredString "installation" "evidenceRoot" installation absolutePath
                    let! provider = requiredString "installation" "provider" installation bounded
                    let! model = requiredString "installation" "model" installation bounded
                    let! effort = requiredString "installation" "effort" installation bounded
                    let! sourceGrant = child "sourceGrant" root |> optionResult "persistent-v3-preparation-refused: sourceGrant"
                    if not (exactProperties [| "credentialReference"; "workspaceId"; "producerId"; "streamId"; "role"; "grantId"; "grantGeneration" |] sourceGrant) then
                        return! refuse "sourceGrant is not closed"
                    let! grantCredential = requiredString "sourceGrant" "credentialReference" sourceGrant identity
                    let! workspace = requiredString "sourceGrant" "workspaceId" sourceGrant identity
                    let! producer = requiredString "sourceGrant" "producerId" sourceGrant identity
                    let! stream = requiredString "sourceGrant" "streamId" sourceGrant identity
                    let! role = requiredString "sourceGrant" "role" sourceGrant ((=) "native-collector")
                    let! grantId = requiredString "sourceGrant" "grantId" sourceGrant identity
                    let mutable generation = 0L
                    let mutable generationProperty = Unchecked.defaultof<JsonElement>
                    if not (sourceGrant.TryGetProperty("grantGeneration", &generationProperty))
                       || not (generationProperty.TryGetInt64(&generation)) || generation <= 0L then
                        return! refuse "sourceGrant.grantGeneration"
                    if grantCredential <> credential then return! refuse "sourceGrant credential differs"
                    let! sourceReference = child "sourceReference" root |> optionResult "persistent-v3-preparation-refused: sourceReference"
                    if not (exactProperties [| "schema"; "profileSha256"; "nativeSourceVolume"; "developmentTarget"; "collectorReadOnlyTarget"; "readerProfileSha256"; "captureQualified"; "verifierRuntimeManifestSha256" |] sourceReference)
                       || string "schema" sourceReference <> Some "fsgg.telemetry.persistent-source-references/3"
                       || boolean "captureQualified" sourceReference <> Some false then
                        return! refuse "sourceReference is not closed inactive v3"
                    let! profileSha = requiredString "sourceReference" "profileSha256" sourceReference sha64
                    let! volume = requiredString "sourceReference" "nativeSourceVolume" sourceReference identity
                    let! developmentTarget = requiredString "sourceReference" "developmentTarget" sourceReference absolutePath
                    let! collectorTarget = requiredString "sourceReference" "collectorReadOnlyTarget" sourceReference absolutePath
                    let! readerProfileSha = requiredString "sourceReference" "readerProfileSha256" sourceReference sha64
                    let! declaredRuntimeManifest = requiredString "sourceReference" "verifierRuntimeManifestSha256" sourceReference sha64
                    if collectorTarget <> codexHome then return! refuse "sourceReference collector path differs"
                    let! verifier = child "verifierRuntime" root |> optionResult "persistent-v3-preparation-refused: verifierRuntime"
                    if not (exactProperties [| "schema"; "sourceRevision"; "runtimeImageDigest"; "runtimeExecutablePath"; "runtimeExecutableSha256"; "modulePath"; "moduleSha256"; "manifestPath"; "manifestSha256"; "files" |] verifier)
                       || string "schema" verifier <> Some "fsgg.telemetry.native-verifier-runtime/1" then
                        return! refuse "verifierRuntime is not closed v1"
                    let! verifierRevision = requiredString "verifierRuntime" "sourceRevision" verifier sha40
                    let! imageDigest = requiredString "verifierRuntime" "runtimeImageDigest" verifier (fun v -> v.StartsWith("sha256:") && sha64 (v.Substring 7))
                    let! runtime = requiredString "verifierRuntime" "runtimeExecutablePath" verifier absolutePath
                    let! runtimeSha = requiredString "verifierRuntime" "runtimeExecutableSha256" verifier sha64
                    let! modulePath = requiredString "verifierRuntime" "modulePath" verifier absolutePath
                    let! moduleSha = requiredString "verifierRuntime" "moduleSha256" verifier ((=) CanonicalVerifierModuleSha256)
                    let! manifestPath = requiredString "verifierRuntime" "manifestPath" verifier absolutePath
                    let! manifestSha = requiredString "verifierRuntime" "manifestSha256" verifier sha64
                    if manifestSha <> declaredRuntimeManifest then return! refuse "sourceReference manifest differs"
                    let! files = array "files" verifier |> optionResult "persistent-v3-preparation-refused: verifierRuntime.files"
                    if files.Length = 0 || files.Length > 4096 then return! refuse "runtime inventory count"
                    let mutable previous = null
                    let mutable total = 0L
                    let mutable runtimeSeen = false
                    let mutable moduleSeen = false
                    let rows = ResizeArray<_>()
                    for file in files do
                        if not (exactProperties [| "path"; "bytes"; "sha256" |] file) then return! refuse "runtime inventory entry is not closed"
                        let! path = requiredString "runtime file" "path" file absolutePath
                        let! digest = requiredString "runtime file" "sha256" file sha64
                        let mutable size = 0L
                        let mutable sizeProperty = Unchecked.defaultof<JsonElement>
                        if not (file.TryGetProperty("bytes", &sizeProperty))
                           || sizeProperty.ValueKind <> JsonValueKind.Number
                           || not (sizeProperty.TryGetInt64(&size)) || size <= 0L then
                            return! refuse "runtime inventory bytes"
                        if not (isNull previous) && StringComparer.Ordinal.Compare(previous, path) >= 0 then return! refuse "runtime inventory order"
                        if total > 512L * 1024L * 1024L - size then return! refuse "runtime inventory size"
                        previous <- path; total <- total + size
                        runtimeSeen <- runtimeSeen || (path = runtime && digest = runtimeSha)
                        moduleSeen <- moduleSeen || (path = modulePath && digest = moduleSha)
                        rows.Add {| path = path; bytes = size; sha256 = digest |}
                    if not runtimeSeen || not moduleSeen then return! refuse "runtime inventory omits required code"
                    let! recovery = child "recovery" root |> optionResult "persistent-v3-preparation-refused: recovery"
                    if not (exactProperties [| "coverage"; "requiredPrivateInputs" |] recovery) then return! refuse "recovery is not closed"
                    let! coverageElements = array "coverage" recovery |> optionResult "persistent-v3-preparation-refused: recovery.coverage"
                    let! coverage = strings "recovery.coverage" coverageElements
                    let! privateElements = array "requiredPrivateInputs" recovery |> optionResult "persistent-v3-preparation-refused: recovery.requiredPrivateInputs"
                    let! privateInputs = strings "recovery.requiredPrivateInputs" privateElements
                    if coverage <> expectedRecoveryCoverage || privateInputs <> expectedPrivateInputs then return! refuse "recovery coverage differs"
                    let runtimeManifestBytes =
                        JsonSerializer.SerializeToUtf8Bytes
                            {| schema = "fsgg.telemetry.native-verifier-runtime/1"; sourceRevision = verifierRevision
                               runtimeImageDigest = imageDigest; runtimeExecutablePath = runtime; modulePath = modulePath
                               files = rows.ToArray() |}
                    if sha256 runtimeManifestBytes <> manifestSha then return! refuse "verifier runtime manifest bytes differ"
                    let sourceReferenceBytes =
                        JsonSerializer.SerializeToUtf8Bytes
                            {| schema = "fsgg.telemetry.persistent-source-references/3"; profileSha256 = profileSha
                               nativeSourceVolume = volume; developmentTarget = developmentTarget
                               collectorReadOnlyTarget = collectorTarget; readerProfileSha256 = readerProfileSha
                               captureQualified = false; verifierRuntimeManifestSha256 = manifestSha |}
                    let managerArguments =
                        [| "install-native-collector"; "--host-config"; hostConfig; "--credential-reference"; credential
                           "--executable"; executable; "--executable-sha256"; executableSha; "--installation-version"; "3"
                           "--codex-home"; codexHome; "--evidence-root"; evidenceRoot; "--provider"; provider
                           "--model"; model; "--effort"; effort; "--verifier-runtime"; runtime
                           "--verifier-runtime-sha256"; runtimeSha; "--verifier-module"; modulePath
                           "--verifier-module-sha256"; moduleSha; "--verifier-runtime-manifest"; manifestPath
                           "--verifier-runtime-manifest-sha256"; manifestSha |]
                    return JsonSerializer.SerializeToUtf8Bytes
                        {| schema = "fsgg.telemetry.persistent-v3-preparation/1"
                           status = "prepared-inactive"; identityClass = identityClass
                           activationAuthorized = false; captureQualified = false; installed = false; custodyEstablished = false
                           manager = {| sourceRevision = managerRevision; sourceTree = managerTree; artifactUri = managerUri; artifactSha256 = managerDigest; manifestSha256 = managerManifest |}
                           host = {| sourceRevision = hostRevision; sourceTree = hostTree; artifactUri = hostUri; artifactSha256 = hostDigest; manifestSha256 = hostManifest; journalSha256 = hostJournal |}
                           installationVersion = 3; managerArguments = managerArguments
                           sourceGrant = {| credentialReference = grantCredential; workspaceId = workspace; producerId = producer; streamId = stream; role = role; grantId = grantId; grantGeneration = generation |}
                           sourceReference = {| schema = "fsgg.telemetry.persistent-source-references/3"; profileSha256 = profileSha; nativeSourceVolume = volume; developmentTarget = developmentTarget; collectorReadOnlyTarget = collectorTarget; readerProfileSha256 = readerProfileSha; captureQualified = false; verifierRuntimeManifestSha256 = manifestSha |}
                           sourceReferenceBytesBase64 = Convert.ToBase64String sourceReferenceBytes
                           sourceReferenceSha256 = sha256 sourceReferenceBytes
                           verifierRuntime = {| schema = "fsgg.telemetry.native-verifier-runtime/1"; sourceRevision = verifierRevision; runtimeImageDigest = imageDigest; runtimeExecutablePath = runtime; runtimeExecutableSha256 = runtimeSha; modulePath = modulePath; moduleSha256 = moduleSha; manifestPath = manifestPath; manifestSha256 = manifestSha; files = rows.ToArray() |}
                           verifierRuntimeManifestBytesBase64 = Convert.ToBase64String runtimeManifestBytes
                           verifierRuntimeManifestSha256 = sha256 runtimeManifestBytes
                           recoveryCoverage = coverage; requiredPrivateInputs = privateInputs
                           nativeAcceptanceClaimed = false |}
                }
        with
        | :? JsonException -> refuse "invalid JSON"
        | :? ArgumentException -> refuse "invalid input"
        | :? InvalidOperationException -> refuse "invalid input kind"
