module NewSddWorkspace.ProviderCatalog

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

let supportedSchemaVersion = 2
let supportedContractVersion = "3.0.0"
let catalogRelativePath = "providers/polyglot.catalog.json"

/// The catalog is an explicitly selected local file for SDD, which captures at most 1 MiB of it.
let private maximumCatalogBytes = 1024 * 1024
let private maximumProviders = 256
let private maximumParameters = 64
let private maximumListItems = 256

type ParameterKind =
    | Text
    | Choice
    | ExactVersion

type Validation =
    { NonEmpty: bool
      MinLength: int
      MaxLength: int
      AllowedValues: string list }

type Parameter =
    { Key: string
      Kind: ParameterKind
      Required: bool
      Prompt: string
      Help: string
      Default: string option
      Values: string list
      Validation: Validation }

type Identities =
    { RawName: string
      PackageIdentity: string
      CodeIdentifier: string }

type Tool =
    { Id: string
      Version: string
      Platforms: string list }

type Provider =
    { Id: string
      DisplayName: string
      Help: string
      Language: string
      ProductShape: string
      ContractVersion: string
      TemplateSource: string
      TemplateId: string
      Platforms: string list
      Parameters: Parameter list
      Identities: Identities
      Tools: Tool list
      CapabilityIds: string list
      Skills: string list }

type Catalog =
    { Id: string
      Revision: string
      Digest: string
      Providers: Provider list }

type Loaded =
    { Catalog: Catalog
      Bytes: byte[]
      RawDigest: string
      Source: string }

type Inputs =
    { CatalogFile: string option
      CatalogSha256: string option
      TemplateArchive: string option
      TemplateSha256: string option
      AdmissionPolicy: string option
      AdmissionPolicySha256: string option
      Params: (string * string) list
      DryRun: bool }

let emptyInputs =
    { CatalogFile = None
      CatalogSha256 = None
      TemplateArchive = None
      TemplateSha256 = None
      AdmissionPolicy = None
      AdmissionPolicySha256 = None
      Params = []
      DryRun = false }

let sha256Digest (bytes: byte[]) =
    "sha256:" + Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()

let normalizeDigest (value: string) : Result<string, string> =
    let text = if isNull value then "" else value.Trim()
    let hex = if text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) then text.Substring 7 else text
    if Regex.IsMatch(hex, @"\A[0-9A-Fa-f]{64}\z") then Ok("sha256:" + hex.ToLowerInvariant())
    else Error "a digest is sha256: followed by exactly 64 hexadecimal digits"

let isExactVersion (value: string) =
    not (String.IsNullOrEmpty value)
    && value.Length <= 128
    && Regex.IsMatch(value, @"\A[0-9][A-Za-z0-9._+-]*\z")
    && (value.Split('.') |> Array.forall (fun part -> part <> "" && part <> "x" && part <> "X"))

// ── Strict reading ───────────────────────────────────────────────────────────────────────────────

exception private Refused of string

let private refuse path message : 'a = raise (Refused(sprintf "%s: %s" path message))

let private describeKind (kind: JsonValueKind) = kind.ToString().ToLowerInvariant()

/// Read a closed object: every property must be known, none may repeat, and the required ones must exist.
let private closedObject (path: string) (allowed: string list) (required: string list) (element: JsonElement) =
    if element.ValueKind <> JsonValueKind.Object then
        refuse path (sprintf "expected an object, found %s" (describeKind element.ValueKind))
    let seen = Collections.Generic.Dictionary<string, JsonElement>(StringComparer.Ordinal)
    for property in element.EnumerateObject() do
        if not (List.contains property.Name allowed) then
            refuse (path + "." + property.Name) "Unknown field."
        if not (seen.TryAdd(property.Name, property.Value)) then
            refuse (path + "." + property.Name) "Duplicate field."
    for name in required do
        if not (seen.ContainsKey name) then
            refuse (path + "." + name) "A required field is missing."
    seen

let private text (path: string) (element: JsonElement) : string =
    if element.ValueKind <> JsonValueKind.String then
        refuse path (sprintf "expected a string, found %s" (describeKind element.ValueKind))
    else
        element.GetString()

let private nonblank (path: string) (element: JsonElement) : string =
    let value = text path element
    if String.IsNullOrWhiteSpace value then refuse path "A nonblank value is required." else value

let private flag (path: string) (element: JsonElement) : bool =
    match element.ValueKind with
    | JsonValueKind.True -> true
    | JsonValueKind.False -> false
    | other -> refuse path (sprintf "expected a boolean, found %s" (describeKind other))

let private whole (path: string) (element: JsonElement) : int =
    if element.ValueKind <> JsonValueKind.Number then
        refuse path (sprintf "expected an integer, found %s" (describeKind element.ValueKind))
    else
        match element.TryGetInt32() with
        | true, value -> value
        | _ -> refuse path "expected a 32-bit integer"

let private items (path: string) (element: JsonElement) : JsonElement list =
    if element.ValueKind <> JsonValueKind.Array then
        refuse path (sprintf "expected an array, found %s" (describeKind element.ValueKind))
    else
        let all = [ for item in element.EnumerateArray() -> item ]
        if all.Length > maximumListItems then refuse path "The list exceeds its bound." else all

let private strings (path: string) (element: JsonElement) : string list =
    items path element |> List.mapi (fun index item -> text (sprintf "%s[%d]" path index) item)

let private distinct (path: string) (values: string list) =
    match values |> List.countBy id |> List.tryFind (fun (_, count) -> count > 1) with
    | Some(value, _) -> refuse path (sprintf "Duplicate identifier: %s" value)
    | None -> ()

let private identifier = Regex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.Compiled)

/// Mirrors the SDD rule: a value fits the declared bounds, allow-list and kind.
let validateValue (parameter: Parameter) (value: string) : string option =
    let v = parameter.Validation
    if isNull value then Some "a value is required"
    elif v.NonEmpty && value.Length = 0 then Some "a nonempty value is required"
    elif value.Length < v.MinLength then Some(sprintf "at least %d characters are required" v.MinLength)
    elif value.Length > v.MaxLength then Some(sprintf "at most %d characters are allowed" v.MaxLength)
    elif not (List.isEmpty v.AllowedValues) && not (List.contains value v.AllowedValues) then
        Some(sprintf "must be one of: %s" (String.Join(", ", v.AllowedValues)))
    else
        match parameter.Kind with
        | Text -> None
        | Choice when List.contains value parameter.Values -> None
        | Choice -> Some(sprintf "must be one of: %s" (String.Join(", ", parameter.Values)))
        | ExactVersion when isExactVersion value -> None
        | ExactVersion -> Some "an exact version literal is required (no range, wildcard or floating selector)"

let private readParameter (path: string) (element: JsonElement) : Parameter =
    let fields =
        closedObject
            path
            [ "key"; "kind"; "required"; "prompt"; "help"; "default"; "values"; "validation" ]
            [ "key"; "kind"; "required"; "prompt"; "help"; "values"; "validation" ]
            element
    let key = nonblank (path + ".key") fields["key"]
    let path = path + "[" + key + "]"
    let kind =
        match text (path + ".kind") fields["kind"] with
        | "string" -> Text
        | "enum" -> Choice
        | "exact-version" -> ExactVersion
        | other -> refuse (path + ".kind") (sprintf "Unsupported parameter kind '%s'." other)
    let rules =
        closedObject
            (path + ".validation")
            [ "nonEmpty"; "minLength"; "maxLength"; "allowedValues" ]
            [ "nonEmpty"; "minLength"; "maxLength"; "allowedValues" ]
            fields["validation"]
    let validation =
        { NonEmpty = flag (path + ".validation.nonEmpty") rules["nonEmpty"]
          MinLength = whole (path + ".validation.minLength") rules["minLength"]
          MaxLength = whole (path + ".validation.maxLength") rules["maxLength"]
          AllowedValues = strings (path + ".validation.allowedValues") rules["allowedValues"] }
    if validation.MinLength < 0 || validation.MaxLength < validation.MinLength then
        refuse path "Invalid length bounds."
    distinct (path + ".validation.allowedValues") validation.AllowedValues
    let values = strings (path + ".values") fields["values"]
    distinct (path + ".values") values
    if (kind = Choice && List.isEmpty values) || (kind <> Choice && not (List.isEmpty values)) then
        refuse path "Enum requires values; other kinds prohibit them."
    let defaultValue =
        match fields.TryGetValue "default" with
        | true, element when element.ValueKind = JsonValueKind.Null -> None
        | true, element -> Some(text (path + ".default") element)
        | _ -> None
    let parameter =
        { Key = key
          Kind = kind
          Required = flag (path + ".required") fields["required"]
          Prompt = nonblank (path + ".prompt") fields["prompt"]
          Help = nonblank (path + ".help") fields["help"]
          Default = defaultValue
          Values = values
          Validation = validation }
    match defaultValue |> Option.bind (validateValue parameter) with
    | Some reason -> refuse path (sprintf "Default fails declared validation: %s." reason)
    | None -> parameter

let private readTool (path: string) (element: JsonElement) : Tool =
    let fields = closedObject path [ "id"; "version"; "platforms" ] [ "id"; "version"; "platforms" ] element
    let id = nonblank (path + ".id") fields["id"]
    let version = nonblank (path + ".version") fields["version"]
    if not (isExactVersion version) then refuse (path + ".version") "Tool version must be an exact literal."
    let platforms = strings (path + ".platforms") fields["platforms"]
    if List.isEmpty platforms then refuse (path + ".platforms") "Tool platforms are required."
    { Id = id; Version = version; Platforms = platforms }

let private readProvider (path: string) (element: JsonElement) : Provider =
    let fields =
        closedObject
            path
            [ "id"; "displayName"; "help"; "language"; "productShape"; "descriptorId"; "descriptorRevision"
              "descriptorDigest"; "contractVersion"; "templateSource"; "templateId"; "platforms"; "parameters"
              "identities"; "tools"; "capabilities"; "evidence"; "skills" ]
            [ "id"; "displayName"; "help"; "language"; "productShape"; "descriptorId"; "descriptorRevision"
              "descriptorDigest"; "contractVersion"; "templateSource"; "templateId"; "platforms"; "parameters"
              "identities"; "tools"; "capabilities"; "evidence"; "skills" ]
            element
    let id = nonblank (path + ".id") fields["id"]
    if not (identifier.IsMatch id) then refuse (path + ".id") "A provider id is letters, digits, '.', '_' or '-'."
    let path = path + "[" + id + "]"
    let contract = nonblank (path + ".contractVersion") fields["contractVersion"]
    if contract <> supportedContractVersion then
        refuse (path + ".contractVersion") (sprintf "Only descriptor protocol %s is supported (found %s)." supportedContractVersion contract)
    let platforms = strings (path + ".platforms") fields["platforms"]
    if List.isEmpty platforms then refuse (path + ".platforms") "At least one platform is required."
    distinct (path + ".platforms") platforms
    let parameterElements = items (path + ".parameters") fields["parameters"]
    if parameterElements.Length > maximumParameters then refuse (path + ".parameters") "The list exceeds its bound."
    let parameters = parameterElements |> List.mapi (fun index p -> readParameter (sprintf "%s.parameters[%d]" path index) p)
    distinct (path + ".parameters") (parameters |> List.map (fun p -> p.Key))
    let roles =
        closedObject
            (path + ".identities")
            [ "rawName"; "packageIdentity"; "codeIdentifier" ]
            [ "rawName"; "packageIdentity"; "codeIdentifier" ]
            fields["identities"]
    let identities =
        { RawName = nonblank (path + ".identities.rawName") roles["rawName"]
          PackageIdentity = nonblank (path + ".identities.packageIdentity") roles["packageIdentity"]
          CodeIdentifier = nonblank (path + ".identities.codeIdentifier") roles["codeIdentifier"] }
    let roleKeys = [ identities.RawName; identities.PackageIdentity; identities.CodeIdentifier ]
    distinct (path + ".identities") roleKeys
    for key in roleKeys do
        if not (parameters |> List.exists (fun p -> p.Key = key)) then
            refuse (path + ".identities") (sprintf "Undeclared reference: %s" key)
    let tools = items (path + ".tools") fields["tools"] |> List.mapi (fun index t -> readTool (sprintf "%s.tools[%d]" path index) t)
    distinct (path + ".tools") (tools |> List.map (fun t -> t.Id))
    // Capabilities and evidence are validated in full by SDD; this reader keeps the identities it previews.
    let capabilityIds =
        items (path + ".capabilities") fields["capabilities"]
        |> List.mapi (fun index c ->
            let capability = sprintf "%s.capabilities[%d]" path index
            if c.ValueKind <> JsonValueKind.Object then refuse capability "expected an object"
            else
                match c.TryGetProperty "id" with
                | true, value -> nonblank (capability + ".id") value
                | _ -> refuse (capability + ".id") "A required field is missing.")
    distinct (path + ".capabilities") capabilityIds
    items (path + ".evidence") fields["evidence"] |> List.iteri (fun index e ->
        if e.ValueKind <> JsonValueKind.Object then refuse (sprintf "%s.evidence[%d]" path index) "expected an object")
    let skills = strings (path + ".skills") fields["skills"]
    distinct (path + ".skills") skills
    let templateSource = nonblank (path + ".templateSource") fields["templateSource"]
    { Id = id
      DisplayName = nonblank (path + ".displayName") fields["displayName"]
      Help = nonblank (path + ".help") fields["help"]
      Language = nonblank (path + ".language") fields["language"]
      ProductShape = nonblank (path + ".productShape") fields["productShape"]
      ContractVersion = contract
      TemplateSource = templateSource
      TemplateId = nonblank (path + ".templateId") fields["templateId"]
      Platforms = platforms
      Parameters = parameters
      Identities = identities
      Tools = tools
      CapabilityIds = capabilityIds
      Skills = skills }

let parse (bytes: byte[]) : Result<Catalog, string> =
    if isNull bytes || bytes.Length = 0 then Error "$: the catalog is empty"
    elif bytes.Length > maximumCatalogBytes then Error "$: the catalog exceeds its 1 MiB bound"
    else
        try
            let options = JsonDocumentOptions(MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow)
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), options)
            let fields =
                closedObject
                    "$"
                    [ "schemaVersion"; "id"; "revision"; "digest"; "providers" ]
                    [ "schemaVersion"; "id"; "revision"; "digest"; "providers" ]
                    document.RootElement
            let schema = whole "$.schemaVersion" fields["schemaVersion"]
            if schema <> supportedSchemaVersion then
                refuse "$.schemaVersion" (sprintf "Only catalog schema %d is supported (found %d)." supportedSchemaVersion schema)
            let digest = nonblank "$.digest" fields["digest"]
            if not (Regex.IsMatch(digest, @"\Asha256:[0-9a-f]{64}\z")) then
                refuse "$.digest" "Expected sha256 and 64 lowercase hex digits."
            let providerElements = items "$.providers" fields["providers"]
            if providerElements.Length > maximumProviders then refuse "$.providers" "The list exceeds its bound."
            if providerElements.IsEmpty then refuse "$.providers" "At least one provider is required."
            let providers = providerElements |> List.mapi (fun index p -> readProvider (sprintf "$.providers[%d]" index) p)
            distinct "$.providers" (providers |> List.map (fun p -> p.Id))
            Ok
                { Id = nonblank "$.id" fields["id"]
                  Revision = nonblank "$.revision" fields["revision"]
                  Digest = digest
                  Providers = providers }
        with
        | Refused message -> Error message
        | :? JsonException as error -> Error("$: the catalog is not valid JSON: " + error.Message)
        | :? ArgumentException as error -> Error("$: the catalog is not valid UTF-8 JSON: " + error.Message)

// ── Selection ────────────────────────────────────────────────────────────────────────────────────

let selectable (builtIn: string list) (catalog: Catalog) : Provider list * string list =
    let shadowed, free = catalog.Providers |> List.partition (fun p -> List.contains p.Id builtIn)
    free, shadowed |> List.map (fun p -> p.Id)

let tryFind (id: string) (providers: Provider list) : Provider option =
    providers |> List.tryFind (fun p -> String.Equals(p.Id, id, StringComparison.Ordinal))

let languages (providers: Provider list) : string list =
    providers |> List.map (fun p -> p.Language) |> List.distinct

let shapesOf (language: string) (providers: Provider list) : Provider list =
    providers |> List.filter (fun p -> String.Equals(p.Language, language, StringComparison.Ordinal))

let rawNameKey (provider: Provider) = provider.Identities.RawName

let resolveParameters
    (provider: Provider)
    (product: string)
    (supplied: (string * string) list)
    : Result<(string * string) list, string list>
    =
    let declared = provider.Parameters |> List.map (fun p -> p.Key)
    let duplicated =
        supplied
        |> List.countBy fst
        |> List.filter (fun (_, count) -> count > 1)
        |> List.map (fun (key, _) -> sprintf "parameter '%s' is given more than once" key)
    let unknown =
        supplied
        |> List.map fst
        |> List.filter (fun key -> not (List.contains key declared))
        |> List.distinct
        |> List.map (fun key ->
            sprintf "%s declares no parameter '%s' (declared: %s)" provider.Id key (String.Join(", ", declared)))
    let forwarded, problems =
        provider.Parameters
        |> List.fold
            (fun (chosen, problems) parameter ->
                let explicitValue = supplied |> List.tryFind (fun (key, _) -> key = parameter.Key) |> Option.map snd
                let effective =
                    match explicitValue with
                    | Some value -> Some(value, true)
                    | None when parameter.Key = provider.Identities.RawName && parameter.Default.IsNone -> Some(product, true)
                    | None -> parameter.Default |> Option.map (fun value -> value, false)
                match effective with
                | None when parameter.Required ->
                    chosen, problems @ [ sprintf "%s requires a value (%s); pass --param %s=<value>" parameter.Key parameter.Prompt parameter.Key ]
                | None -> chosen, problems
                | Some(value, forward) ->
                    match validateValue parameter value with
                    | Some reason -> chosen, problems @ [ sprintf "%s is invalid: %s" parameter.Key reason ]
                    | None -> (if forward then chosen @ [ parameter.Key, value ] else chosen), problems)
            ([], [])
    match duplicated @ unknown @ problems with
    | [] -> Ok forwarded
    | all -> Error all

// ── Template archive, platform, policy ──────────────────────────────────────────────────────────

let parseTemplateSource (source: string) : Result<string * string, string> =
    match source.Split([| "::" |], StringSplitOptions.None) with
    | [| id; version |] when Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z") && isExactVersion version -> Ok(id, version)
    | _ -> Error(sprintf "the template source '%s' is not <package id>::<exact version>" source)

let flatContainerUrl (baseUrl: string) (packageId: string) (version: string) : string =
    let id = packageId.ToLowerInvariant()
    let v = version.ToLowerInvariant()
    sprintf "%s/%s/%s/%s.%s.nupkg" (baseUrl.TrimEnd('/')) id v id v

let platformId (os: string) (architecture: string) : string option =
    let family =
        match os.ToLowerInvariant() with
        | "linux" -> Some "linux"
        | "osx" | "macos" -> Some "osx"
        | "windows" -> Some "win"
        | _ -> None
    let arch =
        match architecture.ToLowerInvariant() with
        | "x64" -> Some "x64"
        | "arm64" -> Some "arm64"
        | "x86" -> Some "x86"
        | _ -> None
    match family, arch with
    | Some f, Some a -> Some(f + "-" + a)
    | _ -> None

let checkPlatform (provider: Provider) (platform: string) : Result<unit, string> =
    if List.contains platform provider.Platforms then Ok()
    else
        Error(
            sprintf
                "%s supports %s; this host is %s. No other platform is substituted."
                provider.Id
                (String.Join(", ", provider.Platforms))
                platform
        )

let policyBudgets (policyBytes: byte[]) (provider: Provider) : Result<int * int, string> =
    let packageAndVersion = parseTemplateSource provider.TemplateSource
    try
        use document = JsonDocument.Parse(ReadOnlyMemory<byte>(policyBytes), JsonDocumentOptions(MaxDepth = 16))
        let root = document.RootElement
        if root.ValueKind <> JsonValueKind.Object then
            raise (ArgumentException "the admission policy must be an object")
        let budget (name: string) =
            match root.TryGetProperty name with
            | true, value when value.ValueKind = JsonValueKind.Number ->
                match value.TryGetInt32() with
                | true, seconds when seconds > 0 -> Ok seconds
                | _ -> Error(sprintf "policy %s must be a positive whole number of seconds" name)
            | _ -> Error(sprintf "policy %s is missing" name)
        let associated =
            match packageAndVersion, root.TryGetProperty "archives" with
            | Ok(packageId, version), (true, archives) when archives.ValueKind = JsonValueKind.Array ->
                archives.EnumerateArray()
                |> Seq.exists (fun a ->
                    let read (name: string) =
                        if a.ValueKind <> JsonValueKind.Object then
                            raise (ArgumentException "an archive association must be an object")
                        match a.TryGetProperty name with
                        | true, v when v.ValueKind = JsonValueKind.String -> v.GetString()
                        | _ -> ""
                    read "templateSource" = provider.TemplateSource && read "packageId" = packageId && read "packageVersion" = version)
            | _ -> false
        match packageAndVersion, budget "maximumPreflightSeconds", budget "maximumScaffoldSeconds" with
        | Error message, _, _ -> Error message
        | _, Error message, _
        | _, _, Error message -> Error message
        | _ when not associated ->
            Error(sprintf "the admission policy declares no archive association for %s" provider.TemplateSource)
        | _, Ok preflight, Ok scaffold -> Ok(preflight, scaffold)
    with :? JsonException | :? ArgumentException -> Error "the admission policy is not valid JSON"

// ── Preview and help ─────────────────────────────────────────────────────────────────────────────

let describe (provider: Provider) : string =
    sprintf "%s · %s — %s" provider.Language provider.ProductShape provider.Help

let previewRows (provider: Provider) (effective: (string * string) list) : (string * string) list =
    let value (parameter: Parameter) =
        match effective |> List.tryFind (fun (key, _) -> key = parameter.Key) with
        | Some(_, v) -> v
        | None ->
            match parameter.Default with
            | Some d -> d + " (default)"
            | None -> "(unset)"
    [ "provider", provider.Id
      "language", provider.Language
      "product shape", provider.ProductShape
      "template", provider.TemplateSource
      "platforms", String.Join(", ", provider.Platforms)
      "tools", String.Join(", ", provider.Tools |> List.map (fun t -> t.Id + " " + t.Version))
      "capabilities", String.Join(", ", provider.CapabilityIds)
      "skills", String.Join(", ", provider.Skills) ]
    @ (provider.Parameters |> List.map (fun p -> "param " + p.Key, value p))

// ── Invocation ───────────────────────────────────────────────────────────────────────────────────

type Invocation =
    { CatalogPath: string
      CatalogDigest: string
      ProviderId: string
      TargetRoot: string
      ArchivePath: string
      ArchiveDigest: string
      PolicyPath: string
      PolicyDigest: string
      Platform: string
      TransportExecutable: string
      PreflightSeconds: int
      ScaffoldSeconds: int
      Parameters: (string * string) list
      DryRun: bool }

let private parameterArguments (invocation: Invocation) =
    invocation.Parameters |> List.collect (fun (key, value) -> [ "--param"; key + "=" + value ])

let inspectArguments (invocation: Invocation) : string list =
    [ "catalog"; "inspect"; "--catalog"; invocation.CatalogPath; "--catalog-sha256"; invocation.CatalogDigest
      "--provider"; invocation.ProviderId ]
    @ parameterArguments invocation
    @ [ "--json" ]

let scaffoldArguments (invocation: Invocation) : string list =
    [ "scaffold"
      "--catalog"; invocation.CatalogPath
      "--catalog-sha256"; invocation.CatalogDigest
      "--provider"; invocation.ProviderId
      "--root"; invocation.TargetRoot
      "--template-archive"; invocation.ArchivePath
      "--template-sha256"; invocation.ArchiveDigest
      "--admission-policy"; invocation.PolicyPath
      "--admission-policy-sha256"; invocation.PolicyDigest
      "--platform"; invocation.Platform
      "--transport-executable"; invocation.TransportExecutable
      "--preflight-timeout-seconds"; string invocation.PreflightSeconds
      "--scaffold-timeout-seconds"; string invocation.ScaffoldSeconds ]
    @ parameterArguments invocation
    @ (if invocation.DryRun then [ "--dry-run" ] else [])
    @ [ "--json" ]
