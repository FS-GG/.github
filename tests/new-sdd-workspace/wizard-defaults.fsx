open NewSddWorkspace.Program
open System.Reflection

let graphQlReadBoundary =
    typeof<Options>.Assembly
        .GetType("NewSddWorkspace.Program")
        .GetMethod("isExplicitGraphQlRead", BindingFlags.Static ||| BindingFlags.NonPublic)

if isNull graphQlReadBoundary then
    failwith "the compiled GraphQL effect boundary is missing"

let graphQlReachesGh document =
    graphQlReadBoundary.Invoke(null, [| box document |]) :?> bool

if not (graphQlReachesGh "query($owner:String!){viewer{login}}") then
    failwith "an explicit retained GraphQL query must remain readable"

for disguisedEffect in
    [
        "mutation($id:ID!){updateRepository(input:{repositoryId:$id}){repository{id}}}"
        "# ignored prefix\nmutation($id:ID!){updateRepository(input:{repositoryId:$id}){repository{id}}}"
        "fragment Repo on Repository{id}\nmutation($id:ID!){updateRepository(input:{repositoryId:$id}){repository{...Repo}}}"
        "{viewer{login}}"
        "query NamedRead {viewer{login}}"
        "not-a-graphql-operation"
    ] do
    if graphQlReachesGh disguisedEffect then
        failwithf "a non-allowlisted GraphQL document reached gh: %s" disguisedEffect

let defaults = assembleWizardOptions "./Pong" "Pong"

if defaults.Coordinate then
    failwith "the wizard must defer coordination until repository initialization"

if defaults.Upgrade then
    failwith "the wizard must not silently select the explicit --upgrade behavior"

if defaults.Lifecycle <> "sdd" then
    failwith "the wizard must preserve Standard SDD as the P4 default"

if
    defaults.Template <> "rendering"
    || defaults.Profile <> None
    || defaults.Bundle <> None
    || defaults.NpmPackage <> None
    || defaults.NpmVersion <> None
    || defaults.BindingTarget <> None
then
    failwith "the wizard must select the dependency-free rendering default"

if
    defaults.WorkspaceRepo <> None
    || defaults.BoardOwner <> "FS-GG"
    || defaults.BoardTitle <> "Coordination"
    || defaults.ChoreLocks <> None
then
    failwith "the wizard must not manufacture repository-specific configuration"

let console =
    assembleWizardTemplateOptions "./Tool" "Tool" "console" None None None None

if console.Template <> "console" then
    failwith "the wizard must preserve the selected template"

let bindings =
    assembleWizardTemplateOptions
        "./Interop"
        "Interop"
        "fable-bindings"
        None
        (Some "@babylonjs/core")
        (Some "8.0.0")
        (Some "browser")

if
    bindings.Template <> "fable-bindings"
    || bindings.NpmPackage <> Some "@babylonjs/core"
    || bindings.NpmVersion <> Some "8.0.0"
    || bindings.BindingTarget <> Some "browser"
then
    failwith "the wizard must preserve the selected template's required package closure"

let game =
    assembleWizardTemplateOptions "./Arena" "Arena" "fable-game" (Some "studio") None None None

if game.Template <> "fable-game" || game.Bundle <> Some "studio" then
    failwith "the wizard must preserve the selected fable-game bundle"

// The omission rule reads the selected descriptor's declared lifecycle default. Exercise it on the
// unmodified bytes of tagged Templates descriptors, including the current public ones (#4811).
let descriptorRoot = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "descriptors")
let fixtures =
    System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(descriptorRoot, "sources.json")))
        .RootElement.GetProperty("descriptors").EnumerateArray()
    |> Seq.map (fun row ->
        let file = row.GetProperty("file").GetString()
        let bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(descriptorRoot, file))
        let digest = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes).ToLowerInvariant()
        if digest <> row.GetProperty("sha256").GetString() then
            failwithf "descriptor fixture %s is not the recorded tagged bytes" file
        file, System.Text.UTF8Encoding(false, true).GetString bytes, row.GetProperty("declaredLifecycleDefault").GetString())
    |> Seq.toList

let descriptor name =
    fixtures |> List.find (fun (file, _, _) -> file = name) |> fun (_, text, _) -> text

for file, text, declared in fixtures do
    if declaredLifecycleDefault text <> Some declared then
        failwithf "descriptor %s must read its declared lifecycle default %s" file declared

let current = declaredLifecycleDefault (descriptor "fable-game-0.19.3.providers.yml")
let publicCurrent = declaredLifecycleDefault (descriptor "fable-game-0.19.2.providers.yml")
let d5 = declaredLifecycleDefault (descriptor "fable-game-0.15.0.providers.yml")
let legacy = declaredLifecycleDefault (descriptor "fable-game-0.14.0.providers.yml")

if selectScaffoldLifecycle game current <> "typed-sdd" then
    failwith "the current fable-game descriptor must select Typed SDD on omission"

if selectScaffoldLifecycle game publicCurrent <> "typed-sdd" then
    failwith "the public 0.19.2 fable-game descriptor must select Typed SDD on omission"

if selectScaffoldLifecycle game d5 <> "typed-sdd" then
    failwith "the D.5 fable-game descriptor must select Typed SDD on omission"

if selectScaffoldLifecycle game legacy <> "sdd" then
    failwith "the older fable-game descriptor must preserve Standard SDD"

if selectScaffoldLifecycle game None <> "sdd" then
    failwith "an unreadable fable-game declaration must keep Standard SDD"

if selectScaffoldLifecycle console current <> "sdd" then
    failwith "an unrelated template must preserve Standard SDD"

for explicit in lifecycleTokens do
    let selected = { game with Lifecycle = explicit; LifecycleExplicit = true }
    for declared in [ current; legacy; None ] do
        if selectScaffoldLifecycle selected declared <> explicit then
            failwithf "an explicit %s lifecycle must take precedence over the descriptor default" explicit

// The parser accepts only one unambiguous admitted declaration in the lifecycle item itself.
let row (body: string) = "providers:\n  - name: fable-game\n    parameters:\n" + body
for refused, body in
    [
        "missing default", "      - key: lifecycle\n        required: false\n      - key: bundle\n        default: typed-sdd\n"
        "unknown token", "      - key: lifecycle\n        default: quint\n"
        "duplicate item", "      - key: lifecycle\n        default: typed-sdd\n      - key: lifecycle\n        default: sdd\n"
        "duplicate default", "      - key: lifecycle\n        default: typed-sdd\n        default: sdd\n"
        "commented default", "      - key: lifecycle\n        # default: typed-sdd\n"
        "other key", "      - key: lifecycleMode\n        default: typed-sdd\n"
    ] do
    if declaredLifecycleDefault (row body) <> None then
        failwithf "an unreadable lifecycle declaration (%s) must not be guessed" refused

if declaredLifecycleDefault (row "      - key: \"lifecycle\"   # quoted\r\n        required: false\r\n\r\n        default: 'typed-sdd' # D.5\r\n") <> Some "typed-sdd" then
    failwith "a quoted, commented CRLF lifecycle declaration must be read exactly"

let defaultGame =
    assembleWizardTemplateOptions "./Player" "Player" "fable-game" None None None None

if defaultGame.Bundle <> Some "player" then
    failwith "the wizard must make player the fable-game default"

let recoveryTarget = System.IO.Path.GetFullPath("./workspace with 'quote")

let recovery =
    securityResumeCommand "./workspace with 'quote" [ "--repo"; "acme/app" ]

let expectedTarget = "'" + recoveryTarget.Replace("'", "'\"'\"'") + "'"

if
    recovery
    <> sprintf "new-sdd-workspace secure %s '--repo' 'acme/app'" expectedTarget
then
    failwithf "durable security recovery command lost or misquoted its workspace identity: %s" recovery

printfn "wizard decision/default assembly: ok"
