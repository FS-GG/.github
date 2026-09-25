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

printfn "permission reducer: %d controls passed" passed
