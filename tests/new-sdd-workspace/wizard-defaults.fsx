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

let recoveryTarget = System.IO.Path.GetFullPath("./workspace with 'quote")
let recovery = securityResumeCommand "./workspace with 'quote" [ "--repo"; "acme/app" ]
let expectedTarget = "'" + recoveryTarget.Replace("'", "'\"'\"'") + "'"
if recovery <> sprintf "new-sdd-workspace secure %s '--repo' 'acme/app'" expectedTarget then
    failwithf "durable security recovery command lost or misquoted its workspace identity: %s" recovery

printfn "wizard decision/default assembly: ok"
