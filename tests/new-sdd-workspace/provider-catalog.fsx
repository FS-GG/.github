// Typed tests for the consumer side of the ref-pinned provider catalog (FS-GG/.github#3010).
// They drive the real compiled module (`--reference:<new-sdd-workspace.dll>`) and the exact bytes of the
// published FS.GG.Workspace.Template 0.20.0 catalog (tag fs-gg-templates/v0.20.0, source 1b9b1ca9),
// plus synthetic catalogs derived from it. Nothing here touches a network, a target or a process.
open System
open System.IO
open System.Text
open System.Text.Json.Nodes
open NewSddWorkspace

let here = __SOURCE_DIRECTORY__
let publishedBytes = File.ReadAllBytes(Path.Combine(here, "catalogs", "polyglot-0.20.0.catalog.json"))

let mutable failures = 0
let check (name: string) (condition: bool) =
    if condition then printfn "PASS  %s" name
    else
        failures <- failures + 1
        printfn "FAIL  %s" name

let expectOk name (result: Result<'a, 'e>) =
    match result with
    | Ok value -> check name true; Some value
    | Error e -> printfn "      %A" e; check name false; None

let expectRefused name (needle: string) (result: Result<'a, string>) =
    match result with
    | Error message when message.Contains needle -> check name true
    | Error message -> printfn "      refused with: %s" message; check (name + " (wrong reason, wanted '" + needle + "')") false
    | Ok _ -> check (name + " (was accepted)") false

let utf8 (text: string) = Encoding.UTF8.GetBytes text
let published = ProviderCatalog.parse publishedBytes |> expectOk "the published 0.20.0 catalog parses" |> Option.get

// ── The published catalog ─────────────────────────────────────────────────────────────────────────
check "raw digest of the exact published bytes" (ProviderCatalog.sha256Digest publishedBytes = "sha256:b779d4f11b972c7643c33cf3e09b45c3e85649d72beed153931e1f89f0d556ca")
check "catalog identity is read from the catalog" (published.Id = "fs-gg-polyglot-cli" && published.Revision = "0.20.0")
check "all four providers are present" ((published.Providers |> List.map (fun p -> p.Id) |> List.sort) = [ "go-cli"; "javascript-cli"; "rust-cli"; "typescript-cli" ])
check "language and product shape are separate values" (published.Providers |> List.forall (fun p -> p.Language <> p.Id && p.ProductShape = "executable-cli"))
check "languages are distinct, in catalog order" (ProviderCatalog.languages published.Providers = [ "Go"; "JavaScript"; "Rust"; "TypeScript" ])
check "shapes of one language" ((ProviderCatalog.shapesOf "Rust" published.Providers |> List.map (fun p -> p.Id)) = [ "rust-cli" ])
check "every provider is linux-x64 only and names its template source" (published.Providers |> List.forall (fun p -> p.Platforms = [ "linux-x64" ] && p.TemplateSource = "FS.GG.Workspace.Template::0.20.0"))
check "declared tools and capabilities are carried for the preview" ((ProviderCatalog.tryFind "go-cli" published.Providers |> Option.get).Tools |> List.map (fun t -> t.Id, t.Version) = [ "go", "1.27.3"; "govulncheck", "1.9.0"; "python", "3.14.8" ])
check "eight neutral capabilities per provider" (published.Providers |> List.forall (fun p -> p.CapabilityIds.Length = 8))

let builtIn = [ "rendering"; "console"; "web"; "python"; "fable-game"; "fable-bindings" ]
let selectable, shadowed = ProviderCatalog.selectable builtIn published
check "no published provider is shadowed by a built-in name" (selectable.Length = 4 && shadowed.IsEmpty)

// ── Typed parameters, no provider-specific code ──────────────────────────────────────────────────
let go = ProviderCatalog.tryFind "go-cli" published.Providers |> Option.get
let ts = ProviderCatalog.tryFind "typescript-cli" published.Providers |> Option.get
check "the raw-name role is bound by the descriptor, not by the host" (ProviderCatalog.rawNameKey go = "rawName")

let supplied = [ "packageIdentity", "example.org/explicit/product"; "codeIdentifier", "explicit_greet" ]
match ProviderCatalog.resolveParameters go "Awkward name!? é" supplied |> expectOk "go-cli resolves with an explicit module path" with
| Some forwarded ->
    check "the display name defaults only the raw-name parameter and stays byte-exact" (forwarded |> List.contains ("rawName", "Awkward name!? é"))
    check "no identifier is derived: module path and code identifier are forwarded as given" (forwarded |> List.contains ("packageIdentity", "example.org/explicit/product") && forwarded |> List.contains ("codeIdentifier", "explicit_greet"))
    check "forwarded values follow declared parameter order" ((forwarded |> List.map fst) = [ "codeIdentifier"; "packageIdentity"; "rawName" ])
| None -> ()

let refusedWith needle result =
    match result with
    | Error (problems: string list) -> problems |> List.exists (fun (p: string) -> p.Contains(needle: string))
    | Ok _ -> false

check "a required module path is refused when absent" (ProviderCatalog.resolveParameters go "Tool" [ ("codeIdentifier", "x") ] |> refusedWith "packageIdentity requires a value")
check "an explicit raw name wins over the product name" (ProviderCatalog.resolveParameters ts "Product" ([ ("rawName", "Shown") ] @ supplied) |> function Ok f -> f |> List.contains ("rawName", "Shown") | _ -> false)
check "an unknown parameter is refused, naming the declared ones" (ProviderCatalog.resolveParameters ts "Tool" (supplied @ [ ("bogus", "1") ]) |> refusedWith "declares no parameter 'bogus'")
check "a repeated parameter is refused" (ProviderCatalog.resolveParameters ts "Tool" (supplied @ [ ("codeIdentifier", "y") ]) |> refusedWith "more than once")
check "an empty value for a nonempty parameter is refused" (ProviderCatalog.resolveParameters ts "Tool" [ "packageIdentity", ""; "codeIdentifier", "x" ] |> refusedWith "packageIdentity is invalid")
check "an over-long value is refused" (ProviderCatalog.resolveParameters ts "Tool" [ "packageIdentity", String('a', 257); "codeIdentifier", "x" ] |> refusedWith "at most 256")
check "empty product name is refused through the raw-name bounds" (ProviderCatalog.resolveParameters ts "" supplied |> refusedWith "rawName is invalid")

// A fifth provider added only to the catalog: enum, exact-version and optional string parameters.
let withFifth =
    let root = JsonNode.Parse(Encoding.UTF8.GetString publishedBytes).AsObject()
    let providers = root["providers"].AsArray()
    let clone = JsonNode.Parse(providers[0].ToJsonString()).AsObject()
    clone["id"] <- JsonValue.Create "zig-cli"
    clone["displayName"] <- JsonValue.Create "zig-cli"
    clone["language"] <- JsonValue.Create "Zig"
    clone["productShape"] <- JsonValue.Create "library"
    clone["templateId"] <- JsonValue.Create "fs-gg-zig-cli"
    clone["templateSource"] <- JsonValue.Create "FS.GG.Workspace.Template::0.21.0"
    let parameter (key: string) (kind: string) (required: bool) (values: string list) (defaultValue: string option) =
        let o = JsonObject()
        o["key"] <- JsonValue.Create key
        o["kind"] <- JsonValue.Create kind
        o["required"] <- JsonValue.Create required
        o["prompt"] <- JsonValue.Create("Prompt for " + key)
        o["help"] <- JsonValue.Create("Help for " + key)
        o["values"] <- JsonArray(values |> List.map (fun v -> JsonValue.Create v :> JsonNode) |> Array.ofList)
        let validation = JsonObject()
        validation["nonEmpty"] <- JsonValue.Create true
        validation["minLength"] <- JsonValue.Create 1
        validation["maxLength"] <- JsonValue.Create 32
        validation["allowedValues"] <- JsonArray()
        o["validation"] <- validation
        defaultValue |> Option.iter (fun d -> o["default"] <- JsonValue.Create d)
        o :> JsonNode
    let parameters = clone["parameters"].AsArray()
    parameters.Add(parameter "optimize" "enum" true [ "debug"; "release" ] (Some "release"))
    parameters.Add(parameter "zigVersion" "exact-version" true [] None)
    parameters.Add(parameter "notes" "string" false [] None)
    providers.Add clone
    root.ToJsonString()

let fifthCatalog = ProviderCatalog.parse (utf8 withFifth) |> expectOk "a fifth valid provider parses without any source edit" |> Option.get
let zig = ProviderCatalog.tryFind "zig-cli" (fst (ProviderCatalog.selectable builtIn fifthCatalog)) |> Option.get
check "the fifth provider is selectable by id" (zig.Language = "Zig" && zig.ProductShape = "library")
check "the fifth provider appears in the language list" (ProviderCatalog.languages fifthCatalog.Providers = [ "Go"; "JavaScript"; "Rust"; "TypeScript"; "Zig" ])
let fifthBase = supplied @ [ ("zigVersion", "0.15.2") ]
match ProviderCatalog.resolveParameters zig "Z" fifthBase |> expectOk "enum default and exact version resolve" with
| Some forwarded ->
    check "an enum default is not forwarded, SDD applies it" (not (forwarded |> List.exists (fun (k, _) -> k = "optimize")))
    check "the exact version is forwarded" (forwarded |> List.contains ("zigVersion", "0.15.2"))
| None -> ()
check "an enum value outside its set is refused" (ProviderCatalog.resolveParameters zig "Z" (fifthBase @ [ ("optimize", "fast") ]) |> refusedWith "must be one of: debug, release")
for floating in [ "latest"; "^1.2.3"; "1.x"; "1.*"; ">=1"; "" ] do
    check (sprintf "exact-version refuses '%s'" floating) (ProviderCatalog.resolveParameters zig "Z" (supplied @ [ ("zigVersion", floating) ]) |> function Error _ -> true | Ok _ -> false)
check "an optional parameter may be omitted or supplied" (ProviderCatalog.resolveParameters zig "Z" (fifthBase @ [ ("notes", "hi") ]) |> function Ok f -> f |> List.contains ("notes", "hi") | _ -> false)
check "the preview lists every declared parameter with its source" (
    let rows = ProviderCatalog.previewRows zig [ ("zigVersion", "0.15.2") ]
    rows |> List.contains ("param optimize", "release (default)") && rows |> List.contains ("param notes", "(unset)") && rows |> List.contains ("param zigVersion", "0.15.2"))
check "help text is built from the catalog" ((ProviderCatalog.describe zig).StartsWith "Zig · library")

// A built-in name wins over a catalog entry with the same id, and the shadowing is reported.
let shadowing = withFifth.Replace("\"id\":\"zig-cli\"", "\"id\":\"console\"")
let shadowCatalog = ProviderCatalog.parse (utf8 shadowing) |> expectOk "a catalog entry may repeat a built-in name" |> Option.get
let freeIds, shadowIds = ProviderCatalog.selectable builtIn shadowCatalog
check "the built-in owner keeps the name and the entry is reported shadowed" (shadowIds = [ "console" ] && not (freeIds |> List.exists (fun p -> p.Id = "console")))

// ── Refusals: the whole catalog, before any target ────────────────────────────────────────────────
let mutate (edit: JsonObject -> unit) =
    let root = JsonNode.Parse(Encoding.UTF8.GetString publishedBytes).AsObject()
    edit root
    utf8 (root.ToJsonString())
let provider (root: JsonObject) (index: int) : JsonObject = root.["providers"].AsArray().[index].AsObject()
let parameterOf (root: JsonObject) (providerIndex: int) (parameterIndex: int) : JsonObject =
    (provider root providerIndex).["parameters"].AsArray().[parameterIndex].AsObject()
let rulesOf (root: JsonObject) (providerIndex: int) (parameterIndex: int) : JsonObject =
    (parameterOf root providerIndex parameterIndex).["validation"].AsObject()
let toolOf (root: JsonObject) (providerIndex: int) (toolIndex: int) : JsonObject =
    (provider root providerIndex).["tools"].AsArray().[toolIndex].AsObject()
let refuse name needle edit = expectRefused name needle (ProviderCatalog.parse (mutate edit))

refuse "an unsupported catalog schema" "Only catalog schema 2" (fun r -> r["schemaVersion"] <- JsonValue.Create 3)
refuse "an unsupported descriptor contract" "Only descriptor protocol 3.0.0" (fun r -> (provider r 1).["contractVersion"] <- JsonValue.Create "4.0.0")
refuse "an unknown parameter kind" "Unsupported parameter kind" (fun r -> (parameterOf r 0 0).["kind"] <- JsonValue.Create "regex")
refuse "an unknown root field" "Unknown field" (fun r -> r["extra"] <- JsonValue.Create 1)
refuse "an unknown provider field" "Unknown field" (fun r -> (provider r 0).["entryPoint"] <- JsonValue.Create "x")
refuse "an unknown validation field" "Unknown field" (fun r -> (rulesOf r 0 0).["pattern"] <- JsonValue.Create ".*")
refuse "a missing required provider field" "A required field is missing" (fun r -> (provider r 0).Remove "language" |> ignore)
refuse "a duplicate provider id" "Duplicate identifier: go-cli" (fun r -> (provider r 1).["id"] <- JsonValue.Create "go-cli")
refuse "a duplicate parameter key" "Duplicate identifier: rawName" (fun r -> (parameterOf r 0 0).["key"] <- JsonValue.Create "rawName")
refuse "an identity role naming an undeclared parameter" "Undeclared reference" (fun r -> ((provider r 0).["identities"].AsObject()).["rawName"] <- JsonValue.Create "nope")
refuse "one parameter bound to two identity roles" "Duplicate identifier" (fun r -> ((provider r 0).["identities"].AsObject()).["codeIdentifier"] <- JsonValue.Create "rawName")
refuse "enum without values" "Enum requires values" (fun r -> (parameterOf r 0 0).["kind"] <- JsonValue.Create "enum")
refuse "a default failing its own validation" "Default fails declared validation" (fun r ->
    (parameterOf r 0 0).["default"] <- JsonValue.Create(String('z', 300)))
refuse "inverted length bounds" "Invalid length bounds" (fun r -> (rulesOf r 0 0).["minLength"] <- JsonValue.Create 999)
refuse "a platformless provider" "At least one platform" (fun r -> (provider r 0).["platforms"] <- JsonArray())
refuse "a floating tool version" "exact literal" (fun r -> (toolOf r 0 0).["version"] <- JsonValue.Create "^1.2")
refuse "no providers" "At least one provider" (fun r -> r["providers"] <- JsonArray())
refuse "a malformed catalog digest" "Expected sha256" (fun r -> r["digest"] <- JsonValue.Create "md5:abc")
refuse "a provider id the CLI could not carry" "A provider id is" (fun r -> (provider r 0).["id"] <- JsonValue.Create "bad id")
expectRefused "an empty catalog" "empty" (ProviderCatalog.parse [||])
expectRefused "not JSON" "not valid JSON" (ProviderCatalog.parse (utf8 "{ nope"))
expectRefused "trailing text after the document" "not valid JSON" (ProviderCatalog.parse (utf8 (Encoding.UTF8.GetString publishedBytes + " x")))
expectRefused "a UTF-8 byte order mark" "not valid" (ProviderCatalog.parse (Array.append [| 0xEFuy; 0xBBuy; 0xBFuy |] publishedBytes))
expectRefused "invalid UTF-8" "not valid" (ProviderCatalog.parse (Array.append (utf8 "{\"schemaVersion\":2,\"id\":\"") [| 0xFFuy; 0xFEuy |]))
expectRefused "a catalog beyond its 1 MiB bound" "1 MiB" (ProviderCatalog.parse (Array.zeroCreate (1024 * 1024 + 1)))
expectRefused "a duplicate JSON key is refused, never last-wins" "Duplicate field"
    (ProviderCatalog.parse (utf8 ((Encoding.UTF8.GetString publishedBytes).Replace("\"schemaVersion\":2,", "\"schemaVersion\":2,\"schemaVersion\":2,"))))
// An unusable entry refuses everything: there is no partial listing.
check "one bad entry leaves no usable provider" (
    let bad = mutate (fun r -> (provider r 3).["contractVersion"] <- JsonValue.Create "9.0.0")
    match ProviderCatalog.parse bad with Error _ -> true | Ok _ -> false)

// ── Digests, template source, platform, policy ────────────────────────────────────────────────────
let hex = String('a', 64)
check "a digest is normalized from bare hex" (ProviderCatalog.normalizeDigest hex = Ok("sha256:" + hex))
check "a digest is normalized from upper case" (ProviderCatalog.normalizeDigest ("SHA256:" + hex.ToUpperInvariant()) = Ok("sha256:" + hex))
check "a short digest is refused" (match ProviderCatalog.normalizeDigest "sha256:abc" with Error _ -> true | _ -> false)
check "a non-hex digest is refused" (match ProviderCatalog.normalizeDigest (String('g', 64)) with Error _ -> true | _ -> false)
check "the template source splits into id and exact version" (ProviderCatalog.parseTemplateSource "FS.GG.Workspace.Template::0.20.0" = Ok("FS.GG.Workspace.Template", "0.20.0"))
check "a floating template source is refused" (match ProviderCatalog.parseTemplateSource "FS.GG.Workspace.Template::latest" with Error _ -> true | _ -> false)
check "a source with no version is refused" (match ProviderCatalog.parseTemplateSource "FS.GG.Workspace.Template" with Error _ -> true | _ -> false)
let flatUrl = ProviderCatalog.flatContainerUrl "https://api.nuget.org/v3-flatcontainer/" "FS.GG.Workspace.Template" "0.20.0"
check "the flat-container URL is the lower-cased exact package" (flatUrl = "https://api.nuget.org/v3-flatcontainer/fs.gg.workspace.template/0.20.0/fs.gg.workspace.template.0.20.0.nupkg")
check "platform ids are os-architecture" (ProviderCatalog.platformId "Linux" "X64" = Some "linux-x64" && ProviderCatalog.platformId "OSX" "Arm64" = Some "osx-arm64" && ProviderCatalog.platformId "Windows" "X64" = Some "win-x64")
check "an unknown platform is None" (ProviderCatalog.platformId "FreeBSD" "X64" = None && ProviderCatalog.platformId "Linux" "RiscV64" = None)
check "a declared platform passes" (ProviderCatalog.checkPlatform go "linux-x64" = Ok())
expectRefused "an undeclared platform is refused, naming the supported ones" "supports linux-x64; this host is osx-arm64" (ProviderCatalog.checkPlatform go "osx-arm64" |> Result.map (fun () -> ()))

let policyFor source =
    utf8 (sprintf """{"schemaVersion":1,"maximumPreflightSeconds":120,"maximumScaffoldSeconds":240,"archives":[{"templateSource":"%s","packageId":"FS.GG.Workspace.Template","packageVersion":"0.20.0"}]}""" source)
check "policy budgets are read when the archive association matches" (ProviderCatalog.policyBudgets (policyFor "FS.GG.Workspace.Template::0.20.0") ts = Ok(120, 240))
expectRefused "a policy with no association for the template source" "no archive association" (ProviderCatalog.policyBudgets (policyFor "FS.GG.Workspace.Template::0.19.3") ts |> Result.map ignore)
expectRefused "a policy with a non-positive budget" "positive whole number" (ProviderCatalog.policyBudgets (utf8 """{"maximumPreflightSeconds":0,"maximumScaffoldSeconds":5,"archives":[]}""") ts |> Result.map ignore)
expectRefused "a policy that is not JSON" "not valid JSON" (ProviderCatalog.policyBudgets (utf8 "nope") ts |> Result.map ignore)
expectRefused "a policy array root is refused" "not valid JSON" (ProviderCatalog.policyBudgets (utf8 "[]") ts |> Result.map ignore)
expectRefused "a scalar archive association is refused" "not valid JSON" (ProviderCatalog.policyBudgets (utf8 """{"maximumPreflightSeconds":1,"maximumScaffoldSeconds":2,"archives":[1]}""") ts |> Result.map ignore)

// ── The exact SDD argument lists ──────────────────────────────────────────────────────────────────
let invocation : ProviderCatalog.Invocation =
    { CatalogPath = "/w/catalog.json"; CatalogDigest = "sha256:" + hex; ProviderId = "go-cli"; TargetRoot = "/w/out"
      ArchivePath = "/w/a.nupkg"; ArchiveDigest = "sha256:" + String('b', 64); PolicyPath = "/w/policy.json"; PolicyDigest = "sha256:" + String('c', 64)
      Platform = "linux-x64"; TransportExecutable = "/opt/dotnet/dotnet"; PreflightSeconds = 120; ScaffoldSeconds = 240
      Parameters = [ "rawName", "a b=c"; "packageIdentity", ""; "codeIdentifier", "--x" ]; DryRun = false }
let scaffold = ProviderCatalog.scaffoldArguments invocation
check "scaffold begins with the catalog route flags in order" (scaffold |> List.take 7 = [ "scaffold"; "--catalog"; "/w/catalog.json"; "--catalog-sha256"; "sha256:" + hex; "--provider"; "go-cli" ])
check "scaffold carries target, archive, policy, platform, transport and both budgets" (
    let pairs = scaffold |> List.pairwise
    [ "--root", "/w/out"; "--template-archive", "/w/a.nupkg"; "--admission-policy", "/w/policy.json"; "--platform", "linux-x64"
      "--transport-executable", "/opt/dotnet/dotnet"; "--preflight-timeout-seconds", "120"; "--scaffold-timeout-seconds", "240" ]
    |> List.forall (fun p -> List.contains p pairs))
check "values are forwarded literally: spaces, '=' and an empty value are preserved" (
    scaffold |> List.contains "rawName=a b=c" && scaffold |> List.contains "packageIdentity=" && scaffold |> List.contains "codeIdentifier=--x")
check "scaffold has no dry-run flag unless asked, and always asks for JSON" (not (List.contains "--dry-run" scaffold) && List.last scaffold = "--json")
check "dry run adds exactly the dry-run flag" (ProviderCatalog.scaffoldArguments { invocation with DryRun = true } |> List.contains "--dry-run")
let inspect = ProviderCatalog.inspectArguments invocation
check "inspect is read-only: no target, archive, policy or transport" (
    inspect |> List.take 2 = [ "catalog"; "inspect" ] && not (List.contains "--root" inspect) && not (List.contains "--template-archive" inspect) && not (List.contains "--transport-executable" inspect))
check "inspect selects the provider and forwards the same parameters" (inspect |> List.contains "go-cli" && inspect |> List.contains "rawName=a b=c")

if failures > 0 then
    printfn "\n%d provider-catalog check(s) failed" failures
    exit 1
else
    printfn "\nall provider-catalog checks passed"
