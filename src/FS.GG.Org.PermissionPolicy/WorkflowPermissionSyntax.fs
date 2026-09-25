namespace FS.GG.Org.PermissionPolicy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

type PermissionSyntaxDiagnostic = { Code: string; Path: string }

/// A pure YAML adapter for the permission reducer. Pinned-ref and roster reads remain external.
[<RequireQualifiedAccess>]
module WorkflowPermissionSyntax =
    let private error code path = Error { Code = code; Path = path }

    let private scalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value -> Option.ofObj value.Value
        | _ -> None

    let private isNull (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value ->
            let tag = string value.Tag
            tag = "tag:yaml.org,2002:null"
            || (tag = "?"
                && (obj.ReferenceEquals(value.Value, null)
                    || (value.Style = ScalarStyle.Plain
                        && (value.Value = "" || value.Value = "null" || value.Value = "Null"
                            || value.Value = "NULL" || value.Value = "~"))))
        | _ -> false

    // Keep the same implicit-scalar boundary as the pinned FSC-03 YAML scaffold.
    let private stringScalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value when not (obj.ReferenceEquals(value.Value, null)) ->
            let tag = string value.Tag
            if tag = "tag:yaml.org,2002:str" then Some value.Value
            elif tag <> "?" then None
            elif value.Style <> ScalarStyle.Plain then Some value.Value
            else
                let lower = value.Value.ToLowerInvariant()
                let implicitValue =
                    lower = "" || lower = "null" || lower = "~"
                    || lower = "true" || lower = "false"
                    || lower = ".inf" || lower = "+.inf" || lower = "-.inf" || lower = ".nan"
                    || Regex.IsMatch(value.Value, "^[+-]?(?:0[xX][0-9a-fA-F_]+|0[oO][0-7_]+|(?:[0-9][0-9_]*)(?:\\.[0-9_]*)?(?:[eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant)
                if implicitValue then None else Some value.Value
        | _ -> None

    let private mapping (node: YamlNode) =
        match node with
        | :? YamlMappingNode as value -> Some value
        | _ -> None

    let private memberValue name (mapping: YamlMappingNode) =
        mapping.Children
        |> Seq.tryPick (fun item ->
            if stringScalar item.Key = Some name then Some item.Value else None)

    let private validateTree path (root: YamlNode) =
        let seen = HashSet<YamlNode>(HashIdentity.Reference)
        let pending = Stack<YamlNode>()
        pending.Push root
        let mutable problem = None

        while pending.Count > 0 && problem.IsNone do
            let node = pending.Pop()
            if not (seen.Add node) then
                problem <- Some "yaml-alias"
            elif seen.Count > 10000 then
                problem <- Some "yaml-size"
            else
                match node with
                | :? YamlMappingNode as entries ->
                    let keys = entries.Children |> Seq.map (fun item -> stringScalar item.Key) |> Seq.toList
                    if keys |> List.exists Option.isNone then
                        problem <- Some "yaml-key"
                    elif keys.Length <> (keys |> Set.ofList |> Set.count) then
                        problem <- Some "yaml-invalid"
                    else
                        for item in entries.Children |> Seq.toArray |> Array.rev do
                            pending.Push item.Value
                            pending.Push item.Key
                | :? YamlSequenceNode as sequence ->
                    for child in sequence.Children |> Seq.toArray |> Array.rev do pending.Push child
                | _ -> ()

        match problem with
        | Some code -> error code path
        | None -> Ok root

    let private parse path text =
        try
            let stream = YamlStream()
            use reader = new StringReader(text)
            stream.Load reader
            if stream.Documents.Count <> 1 then
                error "document-count" path
            else
                match validateTree path stream.Documents[0].RootNode with
                | Error diagnostic -> Error diagnostic
                | Ok root ->
                    match mapping root with
                    | Some value -> Ok value
                    | None -> error "root-shape" path
        with
        | :? YamlException -> error "yaml-invalid" path
        | :? ArgumentException -> error "yaml-invalid" path

    let private block (value: YamlMappingNode) =
        match memberValue "permissions" value with
        | None -> Absent
        | Some node when isNull node -> Null
        | Some (:? YamlMappingNode as scopes) ->
            let entries =
                scopes.Children
                |> Seq.map (fun item -> stringScalar item.Key, stringScalar item.Value)
                |> Seq.toList
            if entries |> List.forall (fun (scope, level) -> scope.IsSome && level.IsSome) then
                Scopes(entries |> List.map (fun (scope, level) -> scope.Value, level.Value))
            else UnsupportedShape
        | Some node ->
            match stringScalar node with
            | Some value -> Shorthand value
            | None -> UnsupportedShape

    /// Inspect one caller job by exact ID; the caller/callee uses relationship is resolved elsewhere.
    let caller path jobId text =
        if String.IsNullOrWhiteSpace jobId then error "job-id" path
        else
            parse path text
            |> Result.bind (fun root ->
                match memberValue "jobs" root |> Option.bind mapping with
                | None -> error "jobs-shape" path
                | Some jobs ->
                    match memberValue jobId jobs |> Option.bind mapping with
                    | None -> error "job-shape" path
                    | Some job -> Ok(block root, block job))

    /// Inspect the callee's top-level grant; workflow_call and pinned ref remain separate checks.
    let callee path text = parse path text |> Result.map block
