namespace FS.GG.Org.PermissionPolicy

/// A syntax adapter must preserve absence, null and every mapping entry before reduction.
type PermissionBlock =
    | Absent
    | Null
    | Shorthand of string
    | Scopes of (string * string) list
    | UnsupportedShape

type PermissionLevel =
    | NoAccess
    | Read
    | Write

type UnderGrant =
    { Scope: string
      Required: PermissionLevel
      Granted: PermissionLevel }

type PermissionVerdict =
    | Satisfied
    | UnderGranted of UnderGrant list
    | UnprovenDefault
    | Refused of string

/// Pure caller/callee permission comparison. This does not resolve YAML, refs or GitHub state.
[<RequireQualifiedAccess>]
module Permissions =
    /// Job permissions override workflow permissions. A callee with no declared scopes
    /// imposes no floor, so the caller block need not be parsed in that case.
    val compare: workflow: PermissionBlock -> job: PermissionBlock -> callee: PermissionBlock -> PermissionVerdict
