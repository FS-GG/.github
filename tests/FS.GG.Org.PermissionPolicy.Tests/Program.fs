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
expect "invalid App grant scope refuses" (Error "app-grants-invalid")
    (bind { bindingFacts with AppGrants = Some { appFact with Grants = [ "*", Read ] } })

let bound =
    match bind bindingFacts with
    | Ok value -> value
    | Error code -> failwithf "fixture evidence refused: %s" code
let appRequest: AppTokenRequestFact =
    { Repository = "FS-GG/.github"
      AppIdentity = "pinned-default-app"
      Requested = Scopes [ "contents", "read" ] }
let compareApp request = AppGrantComparison.compare bound request

expect "App request equal to inventory passes" Satisfied
    (compareApp (Some appRequest))
expect "request above installation grant is an undergrant finding"
    (UnderGranted [ { Scope = "contents"; Required = Write; Granted = Read } ])
    (compareApp (Some { appRequest with Requested = Scopes [ "contents", "write" ] }))
expect "ungranted App scope is an undergrant finding"
    (UnderGranted [ { Scope = "issues"; Required = Read; Granted = NoAccess } ])
    (compareApp (Some { appRequest with Requested = Scopes [ "issues", "read" ] }))
let widerInventory =
    match bind { bindingFacts with AppGrants = Some { appFact with Grants = [ "contents", Write ] } } with
    | Ok value -> value
    | Error code -> failwithf "fixture wide inventory refused: %s" code
expect "inventory overgrant covers a narrower request" Satisfied
    (AppGrantComparison.compare widerInventory (Some appRequest))
expect "explicit none request needs no installation grant" Satisfied
    (compareApp (Some { appRequest with Requested = Scopes [ "issues", "none" ] }))
expect "observed App step with absent scope inputs passes" Satisfied
    (compareApp (Some { appRequest with Requested = Absent }))
expect "explicit empty request passes" Satisfied
    (compareApp (Some { appRequest with Requested = Scopes [] }))
expect "missing App request extraction fact refuses" (Refused "app-request-fact-missing")
    (compareApp None)
expect "explicit null App request refuses" (Refused "app-request-null")
    (compareApp (Some { appRequest with Requested = Null }))
expect "unsupported App request shape refuses" (Refused "app-request-shape-unsupported")
    (compareApp (Some { appRequest with Requested = UnsupportedShape }))
expect "App request shorthand refuses" (Refused "app-request-shorthand-unsupported")
    (compareApp (Some { appRequest with Requested = Shorthand "read-all" }))
expect "duplicate App request scope refuses" (Refused "permissions-scope-duplicate")
    (compareApp (Some { appRequest with Requested = Scopes [ "contents", "read"; "contents", "write" ] }))
expect "dynamic App request value refuses" (Refused "app-request-dynamic")
    (compareApp (Some { appRequest with Requested = Scopes [ "contents", "${{ inputs.level }}" ] }))
expect "unknown static App request level refuses" (Refused "permissions-level-unknown")
    (compareApp (Some { appRequest with Requested = Scopes [ "contents", "admin" ] }))
expect "null static App request level refuses" (Refused "permissions-level-unknown")
    (compareApp (Some { appRequest with Requested = Scopes [ "contents", null ] }))
expect "unnormalized App request scope refuses" (Refused "app-request-scope-invalid")
    (compareApp (Some { appRequest with Requested = Scopes [ "pull-requests", "read" ] }))
expect "wrong App identity refuses before comparison" (Refused "app-identity-mismatch")
    (compareApp (Some { appRequest with AppIdentity = "other-app"; Requested = Scopes [] }))
expect "wrong App request repository refuses" (Refused "app-request-repository-mismatch")
    (compareApp (Some { appRequest with Repository = "FS-GG/Other"; Requested = Scopes [] }))

let compareAppWithFacts facts request =
    bind facts |> Result.map (fun value -> AppGrantComparison.compare value request)

expect "missing roster blocks App comparison" (Error "roster-missing")
    (compareAppWithFacts { bindingFacts with Roster = None } (Some appRequest))
expect "missing App inventory blocks comparison" (Error "app-grants-missing")
    (compareAppWithFacts { bindingFacts with AppGrants = None } (Some appRequest))
expect "wrong bound App identity blocks comparison" (Error "app-grants-identity-mismatch")
    (compareAppWithFacts { bindingFacts with AppGrants = Some { appFact with InventoryId = "other-app" } }
        (Some appRequest))

let scanApp text = WorkflowPermissionSyntax.appTokenSteps "authority.yml" text
let compareScanned text =
    AppGrantComparison.compareWorkflow bound "FS-GG/.github" "authority.yml" text
let workflowWithApp =
    "jobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo ready\n      - uses: actions/create-github-app-token@v3\n        with:\n          permission-contents: write\n      - uses: actions/checkout@v4\n"

expect "scanner inspects all steps and selects the App action"
    (Ok { InspectedSteps = 3
          Requests = [ { JobId = "build"; StepIndex = 2; AppIdentitySecret = None; Requested = Scopes [ "contents", "write" ] } ] })
    (scanApp workflowWithApp)
expect "observed App request is bound to the pinned comparator"
    (Ok ({ InspectedSteps = 3
           Requests = [ { JobId = "build"; StepIndex = 2; Verdict = UnderGranted [ { Scope = "contents"; Required = Write; Granted = Read } ] } ] }: AppTokenWorkflowScan))
    (compareScanned workflowWithApp)
expect "each candidate across jobs receives its own verdict"
    (Ok ({ InspectedSteps = 2
           Requests = [ { JobId = "first"; StepIndex = 1; Verdict = Satisfied }
                        { JobId = "second"; StepIndex = 1; Verdict = UnderGranted [ { Scope = "issues"; Required = Read; Granted = NoAccess } ] } ] }: AppTokenWorkflowScan))
    (compareScanned "jobs:\n  first:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with: { permission-contents: read }\n  second:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with: { permission-issues: read }\n")
expect "App action with no with block is observed as no scope inputs"
    (Ok { InspectedSteps = 1
          Requests = [ { JobId = "build"; StepIndex = 1; AppIdentitySecret = None; Requested = Absent } ] })
    (scanApp "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@v3' } ] } }\n")
expect "empty with block is distinct from missing extraction"
    (Ok ({ InspectedSteps = 1
           Requests = [ { JobId = "build"; StepIndex = 1; Verdict = Satisfied } ] }: AppTokenWorkflowScan))
    (compareScanned "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@v3', with: {} } ] } }\n")
expect "dead conditional App action is still inspected"
    (Ok ({ InspectedSteps = 1
           Requests = [ { JobId = "build"; StepIndex = 1; Verdict = UnderGranted [ { Scope = "contents"; Required = Write; Granted = Read } ] } ] }: AppTokenWorkflowScan))
    (compareScanned "jobs:\n  build:\n    steps:\n      - if: false\n        uses: actions/create-github-app-token@v3\n        with: { permission-contents: write }\n")
expect "ordinary non-App action is counted without inventing a request"
    (Ok ({ InspectedSteps = 1; Requests = [] }: AppTokenWorkflowScan))
    (compareScanned "jobs: { build: { steps: [ { uses: 'actions/checkout@v4' } ] } }\n")
expect "valid reusable-call job has no local steps to inspect"
    (Ok { InspectedSteps = 1; Requests = [] })
    (scanApp "jobs:\n  call:\n    uses: FS-GG/.github/.github/workflows/reuse.yml@main\n  build:\n    steps:\n      - run: echo ready\n")
expect "secret-selected App identity stays visible to the comparator"
    (Ok ({ InspectedSteps = 1
           Requests = [ { JobId = "build"; StepIndex = 1; Verdict = Refused "app-identity-mismatch" } ] }: AppTokenWorkflowScan))
    (compareScanned "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          client-id: ${{ secrets.OTHER_APP_CLIENT_ID }}\n          permission-contents: read\n")
syntaxRefused "duplicate App input key refuses" "yaml-invalid"
    (scanApp "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          permission-contents: read\n          permission-contents: write\n")
syntaxRefused "normalized duplicate App scope refuses" "app-permission-duplicate"
    (scanApp "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          permission-pull-requests: read\n          permission-pull_requests: write\n")
syntaxRefused "missing App permission input value refuses" "app-permission-null"
    (scanApp "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@v3', with: { permission-contents: null } } ] } }\n")
syntaxRefused "dynamic App permission input refuses" "app-permission-dynamic"
    (scanApp "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          permission-contents: ${{ inputs.level }}\n")
syntaxRefused "App with input must be a mapping" "app-with-shape"
    (scanApp "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@v3', with: null } ] } }\n")
syntaxRefused "both App identity inputs refuse" "app-identity-ambiguous"
    (scanApp "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          client-id: ${{ secrets.APP_CLIENT_ID }}\n          app-id: ${{ secrets.APP_ID }}\n")
syntaxRefused "dynamic App identity refuses" "app-identity-unsupported"
    (scanApp "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          client-id: ${{ env.APP_CLIENT_ID }}\n")
syntaxRefused "App action missing ref refuses" "app-action-ref-missing"
    (scanApp "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@' } ] } }\n")
syntaxRefused "reusable-call job cannot hide App steps" "job-call-with-steps"
    (scanApp "jobs:\n  call:\n    uses: FS-GG/.github/.github/workflows/reuse.yml@main\n    steps:\n      - uses: actions/create-github-app-token@v3\n")
syntaxRefused "missing ordinary job steps refuses" "steps-missing"
    (scanApp "jobs: { build: { runs-on: ubuntu-latest } }\n")
syntaxRefused "empty reusable-call target refuses" "job-uses-shape"
    (scanApp "jobs: { call: { uses: '' } }\n")
syntaxRefused "null ordinary job steps refuse" "steps-shape"
    (scanApp "jobs: { build: { steps: null } }\n")
syntaxRefused "unobserved null step refuses" "step-shape"
    (scanApp "jobs: { build: { steps: [ null, { uses: 'actions/create-github-app-token@v3' } ] } }\n")
syntaxRefused "step with uses and run refuses" "step-uses-run"
    (scanApp "jobs: { build: { steps: [ { uses: 'actions/create-github-app-token@v3', run: echo skipped } ] } }\n")
syntaxRefused "non-string uses step refuses" "step-uses-shape"
    (scanApp "jobs: { build: { steps: [ { uses: null } ] } }\n")
syntaxRefused "empty uses step refuses" "step-uses-shape"
    (scanApp "jobs: { build: { steps: [ { uses: '' } ] } }\n")
syntaxRefused "empty run step refuses" "step-run-shape"
    (scanApp "jobs: { build: { steps: [ { run: '' } ] } }\n")
syntaxRefused "empty job map refuses" "jobs-empty"
    (scanApp "jobs: {}\n")
syntaxRefused "wrong authority workflow repository refuses comparison" "app-workflow-repository-mismatch"
    (AppGrantComparison.compareWorkflow bound "FS-GG/Other" "authority.yml" workflowWithApp)

let aggregateText =
    "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with: { permission-contents: read }\n"
let expectedJob: WorkflowJobShape =
    { JobId = "build"; IsReusableCall = false; StepCount = 1; AppStepIndices = [ 1 ] }
let expectedWorkflow: AuthorityWorkflowExpectation =
    { Path = ".github/workflows/one.yml"; Jobs = [ expectedJob ] }
let authorityRoster: AuthorityWorkflowRoster =
    { Repository = "FS-GG/.github"; SourceRef = "source-commit"; Workflows = [ expectedWorkflow ] }
let authorityWorkflow: AuthorityWorkflowSnapshot =
    { Repository = "FS-GG/.github"
      Path = ".github/workflows/one.yml"
      SourceRef = "source-commit"
      Text = aggregateText }
let inventorySnapshot: AppInventorySnapshot =
    { SourceRef = "source-commit"; Inventory = appFact }
let aggregateEvidence: AggregatePermissionEvidence =
    { Roster = Some authorityRoster
      Workflows = Some [ authorityWorkflow ]
      Inventories = Some [ inventorySnapshot ] }
let aggregate evidence = PermissionAggregate.evaluate "source-commit" bound evidence

expect "detailed scanner binds job and App step positions"
    (Ok { InspectedSteps = 1; Jobs = [ expectedJob ]
          Requests = [ { JobId = "build"; StepIndex = 1; AppIdentitySecret = None
                         Requested = Scopes [ "contents", "read" ] } ] })
    (WorkflowPermissionSyntax.appTokenStepsDetailed expectedWorkflow.Path aggregateText)
expect "complete authority workflow and inventory facts satisfy the gate" (Ok GateSatisfied)
    (aggregate aggregateEvidence)
let secondWorkflow: AuthorityWorkflowExpectation =
    { Path = ".github/workflows/two.yml"
      Jobs = [ { JobId = "audit"; IsReusableCall = false; StepCount = 1; AppStepIndices = [] } ] }
let secondSnapshot: AuthorityWorkflowSnapshot =
    { Repository = "FS-GG/.github"
      Path = secondWorkflow.Path
      SourceRef = "source-commit"
      Text = "jobs: { audit: { steps: [ { run: echo ready } ] } }\n" }
let twoWorkflowEvidence =
    { aggregateEvidence with
        Roster = Some { authorityRoster with Workflows = [ expectedWorkflow; secondWorkflow ] }
        Workflows = Some [ authorityWorkflow; secondSnapshot ] }
expect "all selected authority workflows are scanned" (Ok GateSatisfied)
    (aggregate twoWorkflowEvidence)
expect "missing authoritative workflow roster refuses" (Error "workflow-roster-missing")
    (aggregate { aggregateEvidence with Roster = None })
expect "omitted selected workflow refuses before any gate verdict" (Error "workflow-missing")
    (aggregate { twoWorkflowEvidence with Workflows = Some [ authorityWorkflow ] })
expect "missing selected job refuses" (Error "workflow-shape-mismatch")
    (aggregate { aggregateEvidence with
                   Roster = Some { authorityRoster with
                                       Workflows = [ { expectedWorkflow with Jobs = [ expectedJob; { expectedJob with JobId = "audit" } ] } ] } })
let duplicateAppText =
    "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with: { permission-contents: read }\n      - uses: actions/create-github-app-token@v3\n        with: { permission-contents: read }\n"
expect "duplicate App step refuses against expected positions" (Error "workflow-shape-mismatch")
    (aggregate { aggregateEvidence with Workflows = Some [ { authorityWorkflow with Text = duplicateAppText } ] })
expect "omitted selected App step refuses" (Error "workflow-shape-mismatch")
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = "jobs: { build: { steps: [ { run: echo ready } ] } }\n" } ] })
expect "duplicated App index in roster refuses" (Error "workflow-roster-invalid")
    (aggregate { aggregateEvidence with
                   Roster = Some { authorityRoster with
                                       Workflows = [ { expectedWorkflow with Jobs = [ { expectedJob with AppStepIndices = [ 1; 1 ] } ] } ] } })
expect "missing inventory facts refuse" (Error "inventories-missing")
    (aggregate { aggregateEvidence with Inventories = None })
expect "wrong default inventory identity refuses" (Error "inventory-missing")
    (aggregate { aggregateEvidence with Inventories = Some [ { inventorySnapshot with Inventory = { appFact with InventoryId = "other-app" } } ] })
expect "wrong bound inventory grants refuse" (Error "inventory-binding-mismatch")
    (aggregate { aggregateEvidence with Inventories = Some [ { inventorySnapshot with Inventory = { appFact with Grants = [ "contents", Write ] } } ] })
expect "duplicate inventory identity refuses" (Error "inventories-duplicate")
    (aggregate { aggregateEvidence with Inventories = Some [ inventorySnapshot; inventorySnapshot ] })
expect "unknown static App request refuses before gate verdict" (Error "app-request-refused:permissions-level-unknown")
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = aggregateText.Replace("permission-contents: read", "permission-contents: admin") } ] })
expect "dynamic App request refuses before gate verdict" (Error "workflow-syntax:app-permission-dynamic")
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = aggregateText.Replace("permission-contents: read", "permission-contents: '${{ inputs.level }}'") } ] })
expect "stale roster source ref refuses" (Error "stale-source-ref")
    (aggregate { aggregateEvidence with Roster = Some { authorityRoster with SourceRef = "old-commit" } })
expect "stale workflow source ref refuses" (Error "stale-source-ref")
    (aggregate { aggregateEvidence with Workflows = Some [ { authorityWorkflow with SourceRef = "old-commit" } ] })
expect "stale inventory source ref refuses" (Error "stale-source-ref")
    (aggregate { aggregateEvidence with Inventories = Some [ { inventorySnapshot with SourceRef = "old-commit" } ] })
let otherAppText =
    "jobs:\n  build:\n    steps:\n      - uses: actions/create-github-app-token@v3\n        with:\n          client-id: ${{ secrets.OTHER_APP_CLIENT_ID }}\n          permission-contents: read\n"
expect "wrong separately custodied App identity needs matching inventory" (Error "inventory-missing")
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = otherAppText } ] })
let otherAppFact = { appFact with InventoryId = "OTHER_APP_CLIENT_ID" }
expect "separately custodied App binds its own inventory" (Ok GateSatisfied)
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = otherAppText } ]
                   Inventories = Some [ inventorySnapshot; { inventorySnapshot with Inventory = otherAppFact } ] })
expect "App over-request yields an aggregate finding"
    (Ok(GateFindings [ UnderGrantFinding(
        ".github/workflows/one.yml [build] App-token step 1",
        [ { Scope = "contents"; Required = Write; Granted = Read } ]) ]))
    (aggregate { aggregateEvidence with
                   Workflows = Some [ { authorityWorkflow with Text = aggregateText.Replace("permission-contents: read", "permission-contents: write") } ] })
let narrowCaller = { bound with Call = { bound.Call with JobPermissions = Scopes [] } }
expect "caller undergrant yields an aggregate finding"
    (Ok(GateFindings [ UnderGrantFinding(
        "FS-GG/FS.GG.Game -> reuse.yml@main",
        [ { Scope = "contents"; Required = Read; Granted = NoAccess } ]) ]))
    (PermissionAggregate.evaluate "source-commit" narrowCaller aggregateEvidence)
let unprovenCaller =
    { bound with Call = { bound.Call with WorkflowPermissions = Absent; JobPermissions = Absent } }
expect "unproven caller default is a gate finding"
    (Ok(GateFindings [ UnprovenDefaultFinding "FS-GG/FS.GG.Game -> reuse.yml@main" ]))
    (PermissionAggregate.evaluate "source-commit" unprovenCaller aggregateEvidence)
expect "non-callable callee refuses aggregate verdict" (Error "callee-syntax:not-callable")
    (PermissionAggregate.evaluate "source-commit" { bound with CalleeText = "on: push\n" } aggregateEvidence)
expect "null workflow fact path refuses without exception" (Error "workflow-source-invalid")
    (aggregate { aggregateEvidence with Workflows = Some [ { authorityWorkflow with Path = null } ] })
expect "incomplete workflow evidence beats an App undergrant finding" (Error "workflow-missing")
    (aggregate { aggregateEvidence with
                   Roster = Some { authorityRoster with
                                       Workflows = [ expectedWorkflow; { expectedWorkflow with Path = ".github/workflows/two.yml" } ] }
                   Workflows = Some [ { authorityWorkflow with Text = aggregateText.Replace("permission-contents: read", "permission-contents: write") } ] })

printfn "permission reducer: %d controls passed" passed
