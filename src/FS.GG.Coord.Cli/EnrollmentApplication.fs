namespace FS.GG.Coord.Cli

module EnrollmentApplication =
    open FS.GG.Coord.GitHub

    let roots = [ "enrollment"; "route-preflight" ]

    let enrollmentVerbs =
        [ "plan", "plan-successor"
          "prepare", "prepare-successor"
          "bind-runtime", "bind-runtime"
          "rebind-reader", "rebind-reader"
          "render-carrier", "render-carrier"
          "resolve-issues", "resolve-issues" ]

    let usage =
        String.concat "\n" [
            "fsgg-coord enrollment plan --request F --native-facts F --repository DIR --template-output NEW --payload-output NEW"
            "fsgg-coord enrollment prepare --request F --native-facts F --repository DIR --template F --payload F"
            "                              --private-output NEW --public-output NEW --receipt-output NEW"
            "fsgg-coord enrollment bind-runtime --private-binding F --repository DIR --merge-sha SHA --runtime-output NEW"
            "fsgg-coord enrollment rebind-reader --installed-reader DLL --known-readers F --record-output NEW"
            "                              [--runtime-binding F --repository DIR --runtime-output NEW] [--scope F --scope-output NEW]"
            "fsgg-coord enrollment render-carrier --receipt F --output NEW"
            "fsgg-coord enrollment resolve-issues --request F --native-facts-output NEW        (read-only GETs)"
            "fsgg-coord route-preflight --route ROUTE --stage STAGE --repository DIR [--online | --native-facts F] ..."
            "  routes: ProgrammeEnrollmentSuccessor (PlanSuccessor PrepareSuccessor BindRuntime)"
            "          RoutineDelivery (OpenPullRequest MergePullRequest)"
            "          PublicationWriterRegistration (RegisterWriter RunWriter)"
            "exit 0 done / all supported, 2 refused, 10 unsupported, 11 unknown (never 0)" ]

    let tryRun (arguments: string list) : int option =
        match arguments with
        | [ root; ("--help" | "-h") ] when List.contains root roots ->
            printfn "%s" usage
            Some 0
        | "enrollment" :: verb :: rest ->
            match List.tryFind (fun (name, _) -> name = verb) enrollmentVerbs with
            // Hosted by this engine assembly, the CLI identity a migration scope names (rebind-reader).
            | Some(_, command) -> Some(V2EnrollmentCommands.runHosted (System.Reflection.Assembly.GetExecutingAssembly()) command (Array.ofList (command :: rest)))
            | None ->
                eprintfn "board-v2 successor refused: MalformedInput"
                Some 2
        | [ "enrollment" ] ->
            eprintfn "board-v2 successor refused: MalformedInput"
            Some 2
        | "route-preflight" :: rest -> Some(V2EnrollmentCommands.run "preflight" (Array.ofList ("preflight" :: rest)))
        | _ -> None
