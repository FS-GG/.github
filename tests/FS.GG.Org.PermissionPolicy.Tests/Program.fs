open FS.GG.Org.PermissionPolicy

let mutable passed = 0

let expect name wanted actual =
    if actual <> wanted then
        failwithf "%s: wanted %A, got %A" name wanted actual

    passed <- passed + 1
    printfn "PASS %s" name

let need = Scopes [ "contents", "read"; "packages", "read" ]
let exact = Scopes [ "contents", "read"; "packages", "read" ]

expect "exact grant" Satisfied (Permissions.compare exact Absent need)
expect "write covers read" Satisfied
    (Permissions.compare (Scopes [ "contents", "write"; "packages", "write" ]) Absent need)
expect "read-all covers both scopes" Satisfied (Permissions.compare (Shorthand "read-all") Absent need)
expect "write-all covers write" Satisfied
    (Permissions.compare (Shorthand "write-all") Absent (Scopes [ "contents", "write" ]))

expect "missing scope is a finding"
    (UnderGranted [ { Scope = "packages"; Required = Read; Granted = NoAccess } ])
    (Permissions.compare (Scopes [ "contents", "read" ]) Absent need)
expect "read cannot cover write"
    (UnderGranted [ { Scope = "contents"; Required = Write; Granted = Read } ])
    (Permissions.compare (Scopes [ "contents", "read" ]) Absent (Scopes [ "contents", "write" ]))
expect "empty block is an explicit zero grant"
    (UnderGranted [ { Scope = "contents"; Required = Read; Granted = NoAccess }
                    { Scope = "packages"; Required = Read; Granted = NoAccess } ])
    (Permissions.compare (Scopes []) Absent need)
expect "missing caller grant is unproven" UnprovenDefault (Permissions.compare Absent Absent need)

expect "job block narrows top-level grant"
    (UnderGranted [ { Scope = "packages"; Required = Read; Granted = NoAccess } ])
    (Permissions.compare exact (Scopes [ "contents", "read" ]) need)
expect "job block widens top-level grant" Satisfied
    (Permissions.compare (Scopes [ "contents", "read" ]) exact need)
expect "null job override is refused" (Refused "permissions-null")
    (Permissions.compare exact Null need)
expect "unknown shorthand is refused" (Refused "permissions-shorthand-unknown")
    (Permissions.compare (Shorthand "read-some") Absent need)
expect "nonmapping shape is refused" (Refused "permissions-shape-unsupported")
    (Permissions.compare UnsupportedShape Absent need)
expect "duplicate scope is refused" (Refused "permissions-scope-duplicate")
    (Permissions.compare (Scopes [ "contents", "read"; "contents", "write" ]) Absent need)
expect "unknown level is refused" (Refused "permissions-level-unknown")
    (Permissions.compare (Scopes [ "contents", "admin" ]) Absent need)
expect "callee null is refused first" (Refused "permissions-null")
    (Permissions.compare exact Absent Null)
expect "callee duplicate is refused" (Refused "permissions-scope-duplicate")
    (Permissions.compare exact Absent (Scopes [ "contents", "read"; "contents", "write" ]))

expect "callee without permissions imposes no floor" Satisfied
    (Permissions.compare Null UnsupportedShape Absent)
expect "callee empty grant imposes no floor" Satisfied
    (Permissions.compare Null UnsupportedShape (Scopes []))
expect "callee explicit none still requires a declared caller block" UnprovenDefault
    (Permissions.compare Absent Absent (Scopes [ "contents", "none" ]))
expect "callee global shorthand requires global caller grant"
    (UnderGranted [ { Scope = "*"; Required = Read; Granted = NoAccess } ])
    (Permissions.compare exact Absent (Shorthand "read-all"))

let caller text = WorkflowPermissionSyntax.caller "caller.yml" "sync" text
let callee text = WorkflowPermissionSyntax.callee "callee.yml" text

let syntaxRefused name code actual =
    match actual with
    | Error diagnostic when diagnostic.Code = code ->
        passed <- passed + 1
        printfn "PASS %s" name
    | _ -> failwithf "%s: expected %s, got %A" name code actual

expect "YAML absent differs from explicit empty"
    (Ok(Absent, Absent))
    (caller "jobs: { sync: { uses: example } }\n")
expect "YAML empty mapping grants nothing"
    (Ok(Scopes [], Absent))
    (caller "permissions: {}\njobs: { sync: { uses: example } }\n")
expect "YAML top-level null stays null"
    (Ok(Null, Absent))
    (caller "permissions: null\njobs: { sync: { uses: example } }\n")
expect "YAML job null overrides top-level grant"
    (Ok(Scopes [ "contents", "read" ], Null))
    (caller "permissions: { contents: read }\njobs: { sync: { permissions: null } }\n")
expect "quoted null is a string, not null"
    (Ok(Shorthand "null", Absent))
    (caller "permissions: 'null'\njobs: { sync: {} }\n")
expect "explicit string-tag null is a string"
    (Ok(Shorthand "null", Absent))
    (caller "permissions: !!str null\njobs: { sync: {} }\n")
expect "YAML scalar grant reaches pure comparison"
    (Ok(Scopes [ "contents", "read" ], Scopes [ "contents", "none" ]))
    (caller "permissions: { contents: read }\njobs: { sync: { permissions: { contents: none } } }\n")
expect "callee top-level grant parses without a caller"
    (Ok(Scopes [ "contents", "read" ]))
    (callee "on: { workflow_call: {} }\npermissions: { contents: read }\n")
expect "callee absent grant stays absent" (Ok Absent)
    (callee "on: { workflow_call: {} }\n")
expect "numeric permission level stays unsupported"
    (Ok(UnsupportedShape, Absent))
    (caller "permissions: { contents: 42 }\njobs: { sync: {} }\n")

let compared text =
    caller text
    |> Result.map (fun (workflow, job) ->
        Permissions.compare workflow job (Scopes [ "contents", "read" ]))

expect "YAML absent caller grant cannot prove startup" (Ok UnprovenDefault)
    (compared "jobs: { sync: {} }\n")
expect "YAML explicit null refuses instead of becoming absence" (Ok(Refused "permissions-null"))
    (compared "permissions: null\njobs: { sync: {} }\n")
expect "YAML quoted null refuses as an unknown shorthand" (Ok(Refused "permissions-shorthand-unknown"))
    (compared "permissions: 'null'\njobs: { sync: {} }\n")
expect "YAML job override can narrow a valid top-level grant"
    (Ok(UnderGranted [ { Scope = "contents"; Required = Read; Granted = NoAccess } ]))
    (compared "permissions: { contents: read }\njobs: { sync: { permissions: {} } }\n")
expect "YAML absent job block inherits the workflow grant" (Ok Satisfied)
    (compared "permissions: { contents: read }\njobs: { sync: {} }\n")

syntaxRefused "duplicate top-level permissions refuses" "yaml-invalid"
    (caller "permissions: { contents: read }\npermissions: {}\njobs: { sync: {} }\n")
syntaxRefused "duplicate nested permission scope refuses" "yaml-invalid"
    (caller "permissions: { contents: read, contents: write }\njobs: { sync: {} }\n")
syntaxRefused "tagged duplicate key refuses" "yaml-invalid"
    (caller "permissions: {}\n!!str permissions: {}\njobs: { sync: {} }\n")
syntaxRefused "duplicate jobs refuse before selection" "yaml-invalid"
    (caller "jobs: { sync: {}, sync: {} }\n")
syntaxRefused "multi-document workflow refuses" "document-count"
    (caller "jobs: { sync: {} }\n---\njobs: { sync: {} }\n")
syntaxRefused "missing jobs refuses" "jobs-shape" (caller "permissions: {}\n")
syntaxRefused "missing selected job refuses" "job-shape" (caller "jobs: { other: {} }\n")
syntaxRefused "non-string mapping key refuses" "yaml-key"
    (caller "!!int permissions: {}\njobs: { sync: {} }\n")
syntaxRefused "alias cycle refuses" "yaml-alias"
    (caller "jobs: { sync: {} }\ncycle: &x [*x]\n")

let callerCall text = WorkflowPermissionSyntax.callerCall "caller.yml" "sync" text
let callableCallee text = WorkflowPermissionSyntax.callableCallee "callee.yml" text
let target = "FS-GG/.github/.github/workflows/reuse.yml@main"
let validCaller = sprintf "permissions: { contents: read }\njobs: { sync: { uses: %s } }\n" target

expect "selected uses target carries exact callee and ref"
    (Ok { Callee = "reuse.yml"; Ref = "main"; WorkflowPermissions = Scopes [ "contents", "read" ]; JobPermissions = Absent })
    (callerCall validCaller)
expect "yaml extension and pinned ref are retained"
    (Ok { Callee = "reuse.yaml"; Ref = "0123456789abcdef"; WorkflowPermissions = Absent; JobPermissions = Scopes [ "contents", "none" ] })
    (callerCall "jobs: { sync: { uses: ' FS-GG/.github/.github/workflows/reuse.yaml@0123456789abcdef ', permissions: { contents: none } } }\n")
syntaxRefused "missing selected uses refuses" "call-target-missing"
    (callerCall "jobs: { sync: {} }\n")
syntaxRefused "null selected uses refuses" "call-target-missing"
    (callerCall "jobs: { sync: { uses: null } }\n")
syntaxRefused "mapping selected uses refuses" "call-target-missing"
    (callerCall "jobs: { sync: { uses: { workflow: reuse.yml } } }\n")
syntaxRefused "foreign organization target refuses" "call-target-unsupported"
    (callerCall "jobs: { sync: { uses: Other/.github/.github/workflows/reuse.yml@main } }\n")
syntaxRefused "missing target ref refuses" "call-target-unsupported"
    (callerCall "jobs: { sync: { uses: FS-GG/.github/.github/workflows/reuse.yml } }\n")
syntaxRefused "target path traversal refuses" "call-target-unsupported"
    (callerCall "jobs: { sync: { uses: FS-GG/.github/.github/workflows/../reuse.yml@main } }\n")
syntaxRefused "callee name containing at sign refuses" "call-target-unsupported"
    (callerCall "jobs: { sync: { uses: FS-GG/.github/.github/workflows/reuse@bad.yml@main } }\n")

expect "mapping workflow_call grants a floor"
    (Ok(Scopes [ "contents", "read" ]))
    (callableCallee "on: { workflow_call: {} }\npermissions: { contents: read }\n")
expect "scalar workflow_call is callable"
    (Ok(Scopes [ "contents", "read" ]))
    (callableCallee "on: workflow_call\npermissions: { contents: read }\n")
expect "sequence workflow_call is callable"
    (Ok(Scopes [ "contents", "read" ]))
    (callableCallee "on: [push, workflow_call]\npermissions: { contents: read }\n")
expect "callable callee without grant inherits caller token"
    (Ok Absent)
    (callableCallee "on: { workflow_call: {} }\n")
expect "callable callee with empty grant imposes no floor"
    (Ok(Scopes []))
    (callableCallee "on: { workflow_call: {} }\npermissions: {}\n")
syntaxRefused "missing on refuses before permission comparison" "on-missing"
    (callableCallee "permissions: { contents: read }\n")
syntaxRefused "push-only callee refuses" "not-callable"
    (callableCallee "on: push\npermissions: { contents: read }\n")
syntaxRefused "null on refuses" "not-callable"
    (callableCallee "on: null\npermissions: { contents: read }\n")
syntaxRefused "workflow_call scalar options refuse" "workflow-call-shape"
    (callableCallee "on: { workflow_call: unsafe }\npermissions: { contents: read }\n")
syntaxRefused "workflow_call sequence options refuse" "workflow-call-shape"
    (callableCallee "on: { workflow_call: [unsafe] }\npermissions: { contents: read }\n")
syntaxRefused "duplicate sequence event refuses" "on-duplicate"
    (callableCallee "on: [workflow_call, workflow_call]\npermissions: { contents: read }\n")
syntaxRefused "non-string sequence event refuses" "on-shape"
    (callableCallee "on: [workflow_call, 42]\npermissions: { contents: read }\n")
syntaxRefused "duplicate on mapping key refuses" "yaml-invalid"
    (callableCallee "on: workflow_call\non: push\npermissions: { contents: read }\n")

let compareCall callerText calleeText =
    callerCall callerText
    |> Result.bind (fun call ->
        callableCallee calleeText
        |> Result.map (fun floor ->
            Permissions.compare call.WorkflowPermissions call.JobPermissions floor))

expect "callee absence imposes no floor on absent caller grant" (Ok Satisfied)
    (compareCall (sprintf "jobs: { sync: { uses: %s } }\n" target)
        "on: workflow_call\n")
expect "call job override narrows a callable callee floor"
    (Ok(UnderGranted [ { Scope = "contents"; Required = Read; Granted = NoAccess } ]))
    (compareCall (sprintf "permissions: { contents: read }\njobs: { sync: { uses: %s, permissions: {} } }\n" target)
        "on: workflow_call\npermissions: { contents: read }\n")
syntaxRefused "non-callable callee cannot yield satisfied verdict" "not-callable"
    (compareCall validCaller "on: push\npermissions: {}\n")

let call =
    match callerCall validCaller with
    | Ok value -> value
    | Error diagnostic -> failwithf "fixture call refused: %A" diagnostic

let roster: RosterFact =
    { Repository = "FS-GG/.github"; Path = "registry/repos.yml"; Repositories = [ "FS-GG/FS.GG.Game" ] }
let calleeFact: CalleeContentFact =
    { Repository = "FS-GG/.github"
      WorkflowPath = ".github/workflows/reuse.yml"
      Ref = "main"
      Origin = WorkingTree
      Text = "on: workflow_call\npermissions: { contents: read }\n" }
let appFact: AppGrantFact =
    { Repository = "FS-GG/.github"
      InventoryId = "pinned-default-app"
      Grants = [ "contents", Read ] }
let bindingFacts: PermissionBindingFacts =
    { CallerRepository = "FS-GG/FS.GG.Game"
      Roster = Some roster
      Callee = Some calleeFact
      AppGrants = Some appFact }
let bind facts = PermissionEvidenceBinding.bind "FS-GG/FS.GG.Game" "pinned-default-app" call facts

expect "exact roster, ref, callee path and App inventory bind"
    (Ok { CallerRepository = "FS-GG/FS.GG.Game"; Call = call; CalleeText = calleeFact.Text; AppGrants = appFact })
    (bind bindingFacts)
expect "missing roster fact refuses" (Error "roster-missing")
    (bind { bindingFacts with Roster = None })
expect "roster from another repository refuses" (Error "roster-source-mismatch")
    (bind { bindingFacts with Roster = Some { roster with Repository = "FS-GG/alias" } })
expect "roster from another path refuses" (Error "roster-source-mismatch")
    (bind { bindingFacts with Roster = Some { roster with Path = "other/repos.yml" } })
expect "unrostered caller refuses" (Error "caller-not-rostered")
    (bind { bindingFacts with Roster = Some { roster with Repositories = [ "FS-GG/Other" ] } })
expect "duplicate roster identity refuses" (Error "roster-invalid")
    (bind { bindingFacts with Roster = Some { roster with Repositories = [ "FS-GG/FS.GG.Game"; "FS-GG/FS.GG.Game" ] } })
expect "caller repository alias refuses" (Error "caller-repository-mismatch")
    (bind { bindingFacts with CallerRepository = "fs-gg/FS.GG.Game" })
expect "missing callee read refuses" (Error "callee-fact-missing")
    (bind { bindingFacts with Callee = None })
expect "callee repository alias refuses" (Error "callee-repository-mismatch")
    (bind { bindingFacts with Callee = Some { calleeFact with Repository = "fs-gg/.github" } })
expect "callee filename substitution refuses" (Error "callee-path-mismatch")
    (bind { bindingFacts with Callee = Some { calleeFact with WorkflowPath = ".github/workflows/other.yml" } })
expect "wrong fetched ref refuses" (Error "callee-ref-mismatch")
    (bind { bindingFacts with Callee = Some { calleeFact with Ref = "v1" } })
expect "main ref cannot use remote read" (Error "callee-origin-mismatch")
    (bind { bindingFacts with Callee = Some { calleeFact with Origin = ExactRefRead } })
let pinnedCall =
    match callerCall "jobs: { sync: { uses: FS-GG/.github/.github/workflows/reuse.yml@v1 } }\n" with
    | Ok value -> value
    | Error diagnostic -> failwithf "fixture pinned call refused: %A" diagnostic
expect "non-main ref cannot borrow working tree"
    (Error "callee-origin-mismatch")
    (PermissionEvidenceBinding.bind "FS-GG/FS.GG.Game" "pinned-default-app" pinnedCall
        { bindingFacts with Callee = Some { calleeFact with Ref = "v1" } })
expect "non-main exact-ref read binds"
    (Ok { CallerRepository = "FS-GG/FS.GG.Game"; Call = pinnedCall; CalleeText = calleeFact.Text; AppGrants = appFact })
    (PermissionEvidenceBinding.bind "FS-GG/FS.GG.Game" "pinned-default-app" pinnedCall
        { bindingFacts with Callee = Some { calleeFact with Ref = "v1"; Origin = ExactRefRead } })
expect "empty callee read refuses" (Error "callee-text-missing")
    (bind { bindingFacts with Callee = Some { calleeFact with Text = " " } })
expect "missing App grant fact refuses" (Error "app-grants-missing")
    (bind { bindingFacts with AppGrants = None })
expect "App inventory alias refuses" (Error "app-grants-source-mismatch")
    (bind { bindingFacts with AppGrants = Some { appFact with Repository = "fs-gg/.github" } })
expect "unidentified App inventory refuses" (Error "app-grants-identity-mismatch")
    (bind { bindingFacts with AppGrants = Some { appFact with InventoryId = "" } })
expect "wrong App inventory identity refuses" (Error "app-grants-identity-mismatch")
    (bind { bindingFacts with AppGrants = Some { appFact with InventoryId = "other-app" } })
expect "empty App grant inventory refuses" (Error "app-grants-invalid")
    (bind { bindingFacts with AppGrants = Some { appFact with Grants = [] } })
expect "duplicate App grant scope refuses" (Error "app-grants-invalid")
    (bind { bindingFacts with AppGrants = Some { appFact with Grants = [ "contents", Read; "contents", Write ] } })

printfn "permission reducer: %d controls passed" passed
