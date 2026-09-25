namespace FS.GG.Org.Policy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

/// A syntax refusal. A later policy evaluator may add findings without changing parser failures.
type SyntaxDiagnostic = { Code: string; Path: string; Message: string }

/// Distinguishes a missing `paths` key from a present, malformed value such as `paths: null`.
type PathsSyntax =
    | Missing
    | Sequence of string list
    | Invalid of string

/// Syntax of one Actions event declaration, before path-agreement policy is evaluated.
type TriggerSyntax = { Declared: bool; Paths: PathsSyntax; HasPathsIgnore: bool }

/// The bounded rule (a) input. Run scalars remain data and never become YAML comments.
type WorkflowSyntax = {
    PullRequest: TriggerSyntax
    Push: TriggerSyntax
    RunScalars: string list
}

/// Reserved policy result shape. This source-only scaffold does not issue an agreement verdict.
type RuleAVerdict =
    | Agreement
    | Finding of string
    | NoVerdict of SyntaxDiagnostic

/// Parses only the syntax needed by the future organization-owned path policy.
module WorkflowSyntax =
    let private error code path message = Error { Code = code; Path = path; Message = message }

    let private scalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value -> Option.ofObj value.Value
        | _ -> None

    let private isNullScalar (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value ->
            let tag = string value.Tag
            tag = "tag:yaml.org,2002:null"
            || (tag <> "tag:yaml.org,2002:str" && (isNull value.Value || (value.Style = ScalarStyle.Plain
                && (value.Value = "" || value.Value = "null" || value.Value = "Null"
                    || value.Value = "NULL" || value.Value = "~"))))
        | _ -> false

    let private stringPattern (node: YamlNode) =
        match node with
        | :? YamlScalarNode as value when not (isNull value.Value) ->
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

    let private entries (node: YamlNode) =
        match node with
        | :? YamlMappingNode as mapping ->
            Some (mapping.Children |> Seq.map (fun item -> item.Key, item.Value) |> Seq.toList)
        | _ -> None

    let private lookup name pairs =
        pairs |> List.tryPick (fun (key, value) -> if scalar key = Some name then Some value else None)

    let private noTrigger = { Declared = false; Paths = Missing; HasPathsIgnore = false }

    let private pathsSyntax (node: YamlNode) =
        match node with
        | :? YamlSequenceNode as sequence ->
            let values = sequence.Children |> Seq.map stringPattern |> Seq.toList
            if values |> List.forall Option.isSome then
                Sequence (values |> List.choose id)
            else Invalid "paths contains a non-string pattern"
        | _ -> Invalid "paths is present but is not a sequence"

    let private triggerSyntax path name (on: (YamlNode * YamlNode) list) =
        match lookup name on with
        | None -> Ok noTrigger
        | Some value when isNullScalar value -> Ok { noTrigger with Declared = true }
        | Some value ->
            match entries value with
            | None -> error "event-shape" path (name + " must be a mapping or null")
            | Some pairs ->
                let paths = lookup "paths" pairs |> Option.map pathsSyntax |> Option.defaultValue Missing
                Ok { Declared = true; Paths = paths; HasPathsIgnore = lookup "paths-ignore" pairs |> Option.isSome }

    let private validEventName (name: string) =
        Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)

    let private onEntries path (node: YamlNode) =
        match entries node with
        | Some pairs ->
            if pairs |> List.forall (fun (key, _) -> scalar key |> Option.exists validEventName) then Ok pairs
            else error "on-shape" path "on mapping contains an invalid event name"
        | None ->
            match node with
            | :? YamlSequenceNode as sequence ->
                let names = sequence.Children |> Seq.map scalar |> Seq.toList
                if names |> List.forall (Option.exists validEventName) then
                    Ok (names |> List.choose id |> List.map (fun name -> YamlScalarNode(name) :> YamlNode, YamlScalarNode() :> YamlNode))
                else error "on-shape" path "on sequence contains a non-scalar event"
            | :? YamlScalarNode as value when not (isNullScalar node) && validEventName value.Value ->
                Ok [ (YamlScalarNode(value.Value) :> YamlNode), (YamlScalarNode() :> YamlNode) ]
            | _ -> error "on-shape" path "on must be an event name, sequence, or mapping"

    let private runScalars path (root: YamlNode) =
        // YamlDotNet resolves aliases to nodes, including cycles. Walk iteratively and
        // refuse repeated nodes before any later syntax reader can recurse into one.
        let visited = HashSet<YamlNode>(HashIdentity.Reference)
        let pending = Stack<YamlNode>()
        let runs = ResizeArray<string>()
        pending.Push(root)
        let mutable diagnostic = None
        while pending.Count > 0 && diagnostic.IsNone do
            let node = pending.Pop()
            if not (visited.Add(node)) then
                diagnostic <- Some { Code = "yaml-alias"; Path = path; Message = "workflow contains a repeated YAML node" }
            elif visited.Count > 10000 then
                diagnostic <- Some { Code = "yaml-size"; Path = path; Message = "workflow contains too many YAML nodes" }
            else
                match node with
                | :? YamlMappingNode as mapping ->
                    for item in mapping.Children |> Seq.toArray |> Array.rev do
                        if scalar item.Key = Some "run" then
                            match scalar item.Value with
                            | Some value -> runs.Add(value)
                            | None -> pending.Push(item.Value)
                        else pending.Push(item.Value)
                | :? YamlSequenceNode as sequence ->
                    for child in sequence.Children |> Seq.toArray |> Array.rev do pending.Push(child)
                | _ -> ()
        match diagnostic with
        | Some value -> Error value
        | None -> Ok (runs |> Seq.toList)

    /// Parse one YAML document without reading files, resolving Actions expressions, or judging parity.
    let inspect path (text: string) : Result<WorkflowSyntax, SyntaxDiagnostic> =
        try
            let stream = YamlStream()
            use reader = new StringReader(text)
            stream.Load(reader)
            if stream.Documents.Count <> 1 then
                error "document-count" path "workflow must contain exactly one YAML document"
            else
                let root = stream.Documents.[0].RootNode
                match entries root with
                | None -> error "root-shape" path "workflow root must be a mapping"
                | Some pairs ->
                    runScalars path root
                    |> Result.bind (fun runs ->
                        match lookup "on" pairs with
                        | None -> error "on-missing" path "workflow has no on declaration"
                        | Some on ->
                            onEntries path on
                            |> Result.bind (fun events ->
                                triggerSyntax path "pull_request" events
                                |> Result.bind (fun pullRequest ->
                                    triggerSyntax path "push" events
                                    |> Result.map (fun push ->
                                        { PullRequest = pullRequest; Push = push; RunScalars = runs }))))
        with
        | :? YamlException as ex -> error "yaml-invalid" path ex.Message
        | :? ArgumentException as ex -> error "yaml-invalid" path ex.Message
