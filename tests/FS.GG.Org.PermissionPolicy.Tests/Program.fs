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

printfn "permission reducer: %d controls passed" passed
