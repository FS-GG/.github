open NewSddWorkspace.Program

let defaults = assembleWizardOptions "./Pong" "Pong"

if defaults.Coordinate then
    failwith "the wizard must defer coordination until repository initialization"

if defaults.Upgrade then
    failwith "the wizard must not silently select the explicit --upgrade behavior"

if defaults.Lifecycle <> "sdd" then
    failwith "the wizard must preserve Standard SDD as the P4 default"

if defaults.Template <> "rendering"
   || defaults.Profile <> None
   || defaults.NpmPackage <> None
   || defaults.NpmVersion <> None
   || defaults.BindingTarget <> None then
    failwith "the wizard must select the dependency-free rendering default"

if defaults.WorkspaceRepo <> None
   || defaults.BoardOwner <> "FS-GG"
   || defaults.BoardTitle <> "Coordination"
   || defaults.ChoreLocks <> None then
    failwith "the wizard must not manufacture repository-specific configuration"

let console = assembleWizardTemplateOptions "./Tool" "Tool" "console" None None None
if console.Template <> "console" then
    failwith "the wizard must preserve the selected template"

let bindings =
    assembleWizardTemplateOptions
        "./Interop"
        "Interop"
        "fable-bindings"
        (Some "@babylonjs/core")
        (Some "8.0.0")
        (Some "browser")
if bindings.Template <> "fable-bindings"
   || bindings.NpmPackage <> Some "@babylonjs/core"
   || bindings.NpmVersion <> Some "8.0.0"
   || bindings.BindingTarget <> Some "browser" then
    failwith "the wizard must preserve the selected template's required package closure"

let recoveryTarget = System.IO.Path.GetFullPath("./workspace with 'quote")
let recovery = securityResumeCommand "./workspace with 'quote" [ "--repo"; "acme/app" ]
let expectedTarget = "'" + recoveryTarget.Replace("'", "'\"'\"'") + "'"
if recovery <> sprintf "new-sdd-workspace secure %s '--repo' 'acme/app'" expectedTarget then
    failwithf "durable security recovery command lost or misquoted its workspace identity: %s" recovery

printfn "wizard decision/default assembly: ok"
