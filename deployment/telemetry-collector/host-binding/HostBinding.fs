namespace Fs.Gg.Telemetry.HostBinding

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

[<CLIMutable>]
type ValidatedHostBinding =
    { Schema: string
      SourceSha: string
      SourceTree: string
      ProfileSha256: string
      OperationId: string
      SourcePinsSha256: string
      ProducerSha256: string
      BindingSha256: string }

module HostBinding =
    [<Literal>]
    let OperationId = "v2-host-01.8a-native-collaboration-v1"

    [<Literal>]
    let ProfileRelativePath = "deployment/telemetry-collector/native-operation-v1.json"

    let SourceFiles =
        [ "qualify_native.py"
          "native_producer_support.py"
          "native-operation-v1.json"
          "native-producer-config.toml" ]

    let private refuse message = raise (BindingRefusal message)
    let private require condition message = if not condition then refuse message
    let private hexPattern length = Regex($"\\A[0-9a-f]{{{length}}}\\z", RegexOptions.CultureInvariant)
    let private isHex length value = not (isNull value) && (hexPattern length).IsMatch(value)

    let private sha256Bytes (bytes: byte array) =
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()

    let private sha256File (path: string) =
        use stream = File.OpenRead(path)
        Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()

    let private gitBlobSha (bytes: byte array) =
        let prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\u0000")
        let value = Array.append prefix bytes
        Convert.ToHexString(SHA1.HashData(value)).ToLowerInvariant()

    let private requireRegular maximum (path: string) =
        let info = FileInfo(path)
        require info.Exists "input-missing"
        require (not (info.Attributes.HasFlag(FileAttributes.ReparsePoint))) "input-symlink-refused"
        require (info.Length > 0L && info.Length <= maximum) "input-size-refused"

    let private runGit (root: string) (arguments: string list) =
        let exitCode, stdout, _ = (OwnedProcessScope.active ()).Run("/usr/bin/git", root, arguments, 10000, 65536, 4096)
        require (exitCode = 0) "git-source-refused"
        stdout.Trim()

    let private properties (element: JsonElement) =
        require (element.ValueKind = JsonValueKind.Object) "json-object-refused"
        let seen = HashSet<string>(StringComparer.Ordinal)
        [ for property in element.EnumerateObject() do
              require (seen.Add(property.Name)) "json-duplicate-key-refused"
              yield property.Name, property.Value ]
        |> Map.ofList

    let private requireKeys expected actual =
        require (Set.ofSeq actual = Set.ofList expected) "json-fields-refused"

    let private stringValue name (fields: Map<string, JsonElement>) =
        require (fields.ContainsKey(name) && fields[name].ValueKind = JsonValueKind.String) "json-string-refused"
        fields[name].GetString()

    let private intValue name (fields: Map<string, JsonElement>) =
        let mutable value = 0
        require (fields.ContainsKey(name) && fields[name].ValueKind = JsonValueKind.Number && fields[name].TryGetInt32(&value)) "json-integer-refused"
        value

    let private stringArray name (fields: Map<string, JsonElement>) =
        require (fields.ContainsKey(name) && fields[name].ValueKind = JsonValueKind.Array) "json-array-refused"
        [ for item in fields[name].EnumerateArray() do
              require (item.ValueKind = JsonValueKind.String) "json-array-item-refused"
              yield item.GetString() ]

    let private requireExact name expected fields =
        require (stringValue name fields = expected) ("profile-" + name + "-refused")

    let private requireDigest name fields =
        require (isHex 64 (stringValue name fields)) ("profile-" + name + "-refused")

    let private validateProfile (bytes: byte array) =
        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 16))
        let root = properties document.RootElement
        requireKeys [ "schema"; "operationId"; "supportedOperations"; "provider"; "model"; "effort"; "prompt"; "native"; "producer"; "runtime"; "network" ] root.Keys
        require (stringValue "schema" root = "fsgg.telemetry.native-operation-profile/1") "profile-schema-refused"
        require (stringValue "operationId" root = OperationId) "profile-operation-refused"
        require (stringArray "supportedOperations" root = [ OperationId; "v2-host-01.8a-readonly-source-compatibility-v1" ]) "profile-supported-operations-refused"
        requireExact "provider" "openai" root
        requireExact "model" "gpt-5.6-sol" root
        requireExact "effort" "medium" root
        requireExact "prompt" "Use the native collaboration spawn_agent tool exactly once with exactly these arguments: message=\"Return exactly NATIVE-CHILD-ACK; do not read files, use any other tool, contact a service, or spawn another agent.\", task_name=native_ack, model=gpt-5.6-sol, reasoning_effort=medium, fork_turns=none. Then use wait_agent exactly once with timeout_ms=30000. Do not use list_agents, send_message, followup_task, interrupt_agent, any other tool, a retry, or a second wait. After the child has one completed turn containing exactly NATIVE-CHILD-ACK, return exactly NATIVE-PARENT-ACK." root
        let native = properties root["native"]
        requireKeys [ "executable"; "sha256"; "bytes"; "version"; "config"; "configSha256"; "protocolSchemaSha256"; "sessionFlagsSha256" ] native.Keys
        requireExact "executable" "/opt/fsgg/codex/codex" native
        requireExact "version" "0.158.0" native
        requireExact "config" "/opt/fsgg/native-producer-config.toml" native
        requireExact "sha256" "167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9" native
        requireDigest "configSha256" native
        requireExact "protocolSchemaSha256" "5742a9a7dd41a8b44dca3138f506e013620d4a93573c792b1e5881c053f169a7" native
        requireExact "sessionFlagsSha256" "6961dbfc2ddce5988b3e567a8d3e4a9680450f25b7648029f790e6a7975fbe49" native
        require (intValue "bytes" native = 286594376) "profile-native-bytes-refused"
        let producer = properties root["producer"]
        requireKeys [ "executable"; "version"; "executableVersion"; "coherentPayloadSha256"; "coherentMarker"; "telemetryConfig"; "workspaceId"; "producerId"; "streamId"; "repository"; "receiverOrigin"; "credentialReference"; "credentialEnvironment"; "spoolRoot"; "feature"; "item"; "rootAttempt"; "childAttempt" ] producer.Keys
        for name, expected in
            [ "executable", "/opt/fsgg/coord/fsgg-coord-engine"
              "version", "0.94.0"
              "executableVersion", "0.94.0.0"
              "coherentMarker", "/opt/fsgg/coord/coherent-content.sha256"
              "telemetryConfig", "/qualification/native/telemetry/roadmap.json"
              "workspaceId", "v2-host-native-qualification"
              "producerId", "native-prospective-v1"
              "streamId", "roadmap"
              "repository", "FS-GG/.github"
              "receiverOrigin", "https://native-receiver:7443/"
              "credentialReference", "native-prospective-v1"
              "credentialEnvironment", "FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1"
              "spoolRoot", "/qualification/native/telemetry/spool"
              "feature", "V2-HOST-01"
              "item", "V2-HOST-01.8a"
              "rootAttempt", "native-operation-root"
              "childAttempt", "native-operation-child" ] do requireExact name expected producer
        requireExact "coherentPayloadSha256" "9b9486a54e014fd5d21b65ed71a00b9021562a56909a1303f89c9646bca4a585" producer
        let runtime = properties root["runtime"]
        requireKeys [ "home"; "codexHome"; "cwd"; "timeoutSeconds"; "maximumLineBytes"; "maximumEvents"; "python" ] runtime.Keys
        for name, expected in [ "home", "/qualification/native"; "codexHome", "/qualification/native/.codex"; "cwd", "/qualification/native/work"; "python", "3.14.0" ] do requireExact name expected runtime
        require (intValue "timeoutSeconds" runtime = 300 && intValue "maximumLineBytes" runtime = 1048576 && intValue "maximumEvents" runtime = 4096) "profile-runtime-bound-refused"
        let network = properties root["network"]
        requireKeys [ "policySchema"; "policyId"; "privateNetworkId"; "httpsProxy"; "noProxy" ] network.Keys
        for name, expected in
            [ "policySchema", "fsgg.telemetry.native-network-policy/1"
              "policyId", "fsgg-native-egress-v1"
              "privateNetworkId", "fsgg-native-private-v1"
              "httpsProxy", "http://native-egress:3128"
              "noProxy", "localhost,127.0.0.1,[::1],native-receiver" ] do requireExact name expected network

    let private validatePins (bytes: byte array) =
        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 4))
        let fields = properties document.RootElement
        requireKeys SourceFiles fields.Keys
        fields
        |> Map.map (fun _ value ->
            require (value.ValueKind = JsonValueKind.String) "source-pin-kind-refused"
            let digest = value.GetString()
            require (isHex 64 digest) "source-pin-digest-refused"
            digest)

    let private producerPath () = Assembly.GetExecutingAssembly().Location

    let construct sourceRoot sourceSha profilePath sourcePinsPath =
        let root = Path.GetFullPath(sourceRoot)
        require (Directory.Exists(root)) "source-root-refused"
        require (isHex 40 sourceSha) "source-sha-refused"
        let profile = Path.GetFullPath(profilePath)
        require (profile = Path.Combine(root, ProfileRelativePath)) "profile-path-refused"
        requireRegular (1024L * 1024L) profile
        requireRegular (64L * 1024L) sourcePinsPath
        let head = runGit root [ "rev-parse"; "HEAD" ]
        require (head = sourceSha) "source-head-drift"
        require (runGit root [ "status"; "--porcelain" ] = "") "source-worktree-dirty"
        let tree = runGit root [ "rev-parse"; sourceSha + "^{tree}" ]
        require (isHex 40 tree) "source-tree-refused"
        let profileBytes = File.ReadAllBytes(profile)
        validateProfile profileBytes
        let pinsBytes = File.ReadAllBytes(sourcePinsPath)
        let pins = validatePins pinsBytes
        let nativeRoot = Path.Combine(root, "deployment/telemetry-collector")
        for name in SourceFiles do
            let path = Path.Combine(nativeRoot, name)
            requireRegular (4L * 1024L * 1024L) path
            let bytes = File.ReadAllBytes(path)
            require (sha256Bytes bytes = pins[name]) "source-pin-content-refused"
            let gitObject = runGit root [ "rev-parse"; sourceSha + ":deployment/telemetry-collector/" + name ]
            require (gitObject = gitBlobSha bytes) "source-blob-refused"
        let profileSha = sha256Bytes profileBytes
        require (pins["native-operation-v1.json"] = profileSha) "profile-source-pin-refused"
        let producer = producerPath ()
        requireRegular (16L * 1024L * 1024L) producer
        let producerSha = sha256File producer
        let sourcePinsSha = sha256Bytes pinsBytes
        let basis = String.concat "\u0000" [ sourceSha; tree; profileSha; OperationId; sourcePinsSha; producerSha ]
        let bindingSha = sha256Bytes (Encoding.UTF8.GetBytes(basis))
        { Schema = "fsgg.telemetry.validated-host-binding/1"
          SourceSha = sourceSha
          SourceTree = tree
          ProfileSha256 = profileSha
          OperationId = OperationId
          SourcePinsSha256 = sourcePinsSha
          ProducerSha256 = producerSha
          BindingSha256 = bindingSha }

    let deriveAdmission (binding: ValidatedHostBinding) nonce =
        require (not (isNull nonce) && Regex.IsMatch(nonce, "\\A[a-z0-9][a-z0-9-]{7,63}\\z")) "run-nonce-refused"
        sha256Bytes (Encoding.UTF8.GetBytes(String.concat "\u0000" [ nonce; binding.SourceSha; binding.ProfileSha256; binding.OperationId ]))

    let verifyAdmission binding nonce expectedBinding candidate =
        let fixedEqual (left: byte array) (right: byte array) =
            CryptographicOperations.FixedTimeEquals(ReadOnlySpan<byte>(left), ReadOnlySpan<byte>(right))
        require (isHex 64 expectedBinding && fixedEqual (Convert.FromHexString(expectedBinding)) (Convert.FromHexString(binding.BindingSha256))) "binding-drift-refused"
        require (isHex 64 candidate) "effect-admission-refused"
        let expected = deriveAdmission binding nonce
        require (fixedEqual (Convert.FromHexString(candidate)) (Convert.FromHexString(expected))) "effect-admission-refused"
