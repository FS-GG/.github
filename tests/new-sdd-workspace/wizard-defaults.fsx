open NewSddWorkspace.Program

let defaults =
    assembleWizardOptions
        "./Pong"
        "Pong"
        "rendering"
        "main"
        true
        false
        (Some "game")
        None
        None
        None
        "FS-GG/Pong"
        "FS-GG"
        "Coordination"
        None

if not defaults.Coordinate then
    failwith "the wizard must keep default-on coordination"

if defaults.Upgrade then
    failwith "the wizard must not silently select the explicit --upgrade behavior"

if defaults.WorkspaceRepo <> Some "FS-GG/Pong"
   || defaults.BoardOwner <> "FS-GG"
   || defaults.BoardTitle <> "Coordination"
   || defaults.ChoreLocks <> None then
    failwith "the wizard did not preserve its default coordination assembly"

let nonDefaultWiring =
    assembleWizardOptions
        "./Product.X"
        "Product.X"
        "rendering"
        "release/v1"
        false
        true
        (Some "app")
        None
        None
        None
        "acme/Product.X"
        "acme"
        "Roadmap"
        (Some "acme/Product.X#5")

if not nonDefaultWiring.Coordinate
   || nonDefaultWiring.Upgrade
   || nonDefaultWiring.WorkspaceRepo <> Some "acme/Product.X"
   || nonDefaultWiring.BoardOwner <> "acme"
   || nonDefaultWiring.BoardTitle <> "Roadmap"
   || nonDefaultWiring.ChoreLocks <> Some "acme/Product.X#5" then
    failwith "the wizard did not preserve explicit non-default coordination values"

printfn "wizard decision/default assembly: ok"
