namespace FS.GG.Org.PermissionPolicy

open System

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
    {
        Scope: string
        Required: PermissionLevel
        Granted: PermissionLevel
    }

type PermissionVerdict =
    | Satisfied
    | UnderGranted of UnderGrant list
    | UnprovenDefault
    | Refused of string

/// Pure caller/callee permission comparison. This does not resolve YAML, refs or GitHub state.
[<RequireQualifiedAccess>]
module Permissions =
    let private level = function
        | "none" -> Some NoAccess
        | "read" -> Some Read
        | "write" -> Some Write
        | _ -> Option.None

    let private rank = function
        | NoAccess -> 0
        | Read -> 1
        | Write -> 2

    let private parse block =
        match block with
        | Absent -> Ok Option.None
        | Null -> Error "permissions-null"
        | UnsupportedShape -> Error "permissions-shape-unsupported"
        | Shorthand "read-all" -> Ok(Some(Map.ofList [ "*", Read ]))
        | Shorthand "write-all" -> Ok(Some(Map.ofList [ "*", Write ]))
        | Shorthand _ -> Error "permissions-shorthand-unknown"
        | Scopes entries ->
            let mutable seen = Set.empty
            let mutable grants = Map.empty
            let mutable error = Option.None

            for scope, value in entries do
                if error.IsNone then
                    if String.IsNullOrWhiteSpace scope || scope = "*" then
                        error <- Some "permissions-scope-invalid"
                    elif Set.contains scope seen then
                        error <- Some "permissions-scope-duplicate"
                    else
                        seen <- Set.add scope seen

                        match level value with
                        | Some parsed -> grants <- Map.add scope parsed grants
                        | Option.None -> error <- Some "permissions-level-unknown"

            match error with
            | Some code -> Error code
            | Option.None -> Ok(Some grants)

    let private grant (grants: Map<string, PermissionLevel>) scope =
        match Map.tryFind "*" grants with
        | Some value -> value
        | Option.None -> Map.tryFind scope grants |> Option.defaultValue NoAccess

    /// Job permissions override workflow permissions. A callee with no declared scopes
    /// imposes no floor, so the caller block need not be parsed in that case.
    let compare workflow job callee =
        match parse callee with
        | Error code -> Refused code
        | Ok Option.None -> Satisfied
        | Ok(Some grants) when Map.isEmpty grants -> Satisfied
        | Ok(Some required) ->
            let effective = if job = Absent then workflow else job

            match parse effective with
            | Error code -> Refused code
            | Ok Option.None -> UnprovenDefault
            | Ok(Some available) ->
                let short =
                    required
                    |> Map.toList
                    |> List.choose (fun (scope, want) ->
                        let got = grant available scope
                        if rank got < rank want then
                            Some { Scope = scope; Required = want; Granted = got }
                        else
                            Option.None)

                if List.isEmpty short then Satisfied else UnderGranted short
