namespace FS.GG.Org.PermissionPolicy

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

type PermissionSyntaxDiagnostic = { Code: string; Path: string }

type ReusableWorkflowCall =
    {
        Callee: string
        Ref: string
        WorkflowPermissions: PermissionBlock
        JobPermissions: PermissionBlock
    }

type AppTokenStep =
    {
        JobId: string
        StepIndex: int
        AppIdentitySecret: string option
        Requested: PermissionBlock
    }

type AppTokenScan =
    {
        InspectedSteps: int
        Requests: AppTokenStep list
    }

/// A pure YAML adapter for the permission reducer. Pinned-ref and roster reads remain external.
[<RequireQualifiedAccess>]
module WorkflowPermissionSyntax =
    let private error code path = Error { Code = code; Path = path }
    let private callTarget =
        Regex("^FS-GG/\\.github/\\.github/workflows/([^@/]+\\.ya?ml)@(.+)$", RegexOptions.CultureInvariant)
    let private appIdentitySecret =
        Regex("^\\$\\{\\{\\s*secrets\\.([A-Z][A-Z0-9_]*)\\s*\\}\\}$", RegexOptions.CultureInvariant)
    let private appTokenAction = "actions/create-github-app-token@"

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

    let private selectedJob path jobId text =
        if String.IsNullOrWhiteSpace jobId then error "job-id" path
        else
            parse path text
            |> Result.bind (fun root ->
                match memberValue "jobs" root |> Option.bind mapping with
                | None -> error "jobs-shape" path
                | Some jobs ->
                    match memberValue jobId jobs |> Option.bind mapping with
                    | None -> error "job-shape" path
                    | Some job -> Ok(root, job))

    /// Inspect one caller job by exact ID; the caller/callee uses relationship is resolved elsewhere.
    let caller path jobId text =
        selectedJob path jobId text
        |> Result.map (fun (root, job) -> block root, block job)

    /// Require the selected job to call the organization's reusable workflow at a stated ref.
    /// Reading that ref and binding the returned callee bytes remain separate provider work.
    let callerCall path jobId text =
        selectedJob path jobId text
        |> Result.bind (fun (root, job) ->
            match memberValue "uses" job |> Option.bind stringScalar with
            | None -> error "call-target-missing" path
            | Some value ->
                let matched = callTarget.Match(value.Trim())
                if not matched.Success then error "call-target-unsupported" path
                else
                    Ok
                        {
                            Callee = matched.Groups[1].Value
                            Ref = matched.Groups[2].Value
                            WorkflowPermissions = block root
                            JobPermissions = block job
                        })

    /// Inspect only the callee's top-level grant; this permissive entry point has no call evidence.
    let callee path text = parse path text |> Result.map block

    /// Require the callee's `workflow_call` declaration before comparing its grant.
    /// This is syntax only: the caller's ref must still resolve to these exact bytes.
    let callableCallee path text =
        parse path text
        |> Result.bind (fun root ->
            match memberValue "on" root with
            | None -> error "on-missing" path
            | Some on ->
                let declared =
                    match on with
                    | :? YamlMappingNode as events ->
                        match memberValue "workflow_call" events with
                        | None -> Ok false
                        | Some value when isNull value || (mapping value).IsSome -> Ok true
                        | Some _ -> error "workflow-call-shape" path
                    | :? YamlSequenceNode as events ->
                        let names = events.Children |> Seq.map stringScalar |> Seq.toList
                        if names |> List.exists Option.isNone then error "on-shape" path
                        elif names.Length <> (names |> Set.ofList |> Set.count) then error "on-duplicate" path
                        else Ok(List.contains (Some "workflow_call") names)
                    | _ when isNull on -> Ok false
                    | _ ->
                        match stringScalar on with
                        | Some name -> Ok(name = "workflow_call")
                        | None -> error "on-shape" path

                declared
                |> Result.bind (fun value -> if value then Ok(block root) else error "not-callable" path))

    let private appRequest path (inputs: YamlMappingNode option) =
        match inputs with
        | None -> Ok(None, Absent)
        | Some values ->
            let client = memberValue "client-id" values
            let legacy = memberValue "app-id" values
            if client.IsSome && legacy.IsSome then error "app-identity-ambiguous" path
            else
                let identity =
                    match client |> Option.orElse legacy with
                    | None -> Ok None
                    | Some value when isNull value -> error "app-identity-null" path
                    | Some value ->
                        match stringScalar value with
                        | None -> error "app-identity-unsupported" path
                        | Some literal ->
                            let matched = appIdentitySecret.Match literal
                            if matched.Success then Ok(Some matched.Groups[1].Value)
                            else error "app-identity-unsupported" path

                identity
                |> Result.bind (fun selected ->
                    let permissions =
                        values.Children
                        |> Seq.choose (fun item ->
                            match stringScalar item.Key with
                            | Some key when key.StartsWith("permission-", StringComparison.Ordinal) ->
                                Some(key.Substring("permission-".Length).Replace('-', '_'), item.Value)
                            | _ -> None)
                        |> Seq.toList
                    let scopes = permissions |> List.map fst
                    if scopes.Length <> (scopes |> Set.ofList |> Set.count) then
                        error "app-permission-duplicate" path
                    else
                        let mutable problem = None
                        let entries =
                            permissions
                            |> List.choose (fun (scope, value) ->
                                if isNull value then
                                    problem <- Some "app-permission-null"
                                    None
                                else
                                    match stringScalar value with
                                    | None ->
                                        problem <- Some "app-permission-shape"
                                        None
                                    | Some level when level.Contains "${{" ->
                                        problem <- Some "app-permission-dynamic"
                                        None
                                    | Some level -> Some(scope, level))
                        match problem with
                        | Some code -> error code path
                        | None -> Ok(selected, if List.isEmpty entries then Absent else Scopes entries))

    /// Inspect every step in every ordinary job, then extract static App-token requests.
    /// A valid reusable-call job has no steps; malformed job and step shapes refuse.
    let appTokenSteps path text =
        parse path text
        |> Result.bind (fun root ->
            match memberValue "jobs" root |> Option.bind mapping with
            | None -> error "jobs-shape" path
            | Some jobs when jobs.Children.Count = 0 -> error "jobs-empty" path
            | Some jobs ->
                let requests = ResizeArray<AppTokenStep>()
                let mutable inspected = 0
                let mutable problem = None
                for item in jobs.Children do
                    if problem.IsNone then
                        let jobId = stringScalar item.Key |> Option.get
                        match mapping item.Value with
                        | None -> problem <- Some "job-shape"
                        | Some job ->
                            let uses = memberValue "uses" job
                            let steps = memberValue "steps" job
                            match uses, steps with
                            | Some _, Some _ -> problem <- Some "job-call-with-steps"
                            | Some call, None when (stringScalar call
                                                    |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))).IsNone ->
                                problem <- Some "job-uses-shape"
                            | Some _, None -> ()
                            | None, None -> problem <- Some "steps-missing"
                            | None, Some (:? YamlSequenceNode as sequence) when sequence.Children.Count = 0 ->
                                problem <- Some "steps-empty"
                            | None, Some (:? YamlSequenceNode as sequence) ->
                                for index, node in sequence.Children |> Seq.indexed do
                                    if problem.IsNone then
                                        inspected <- inspected + 1
                                        match mapping node with
                                        | None -> problem <- Some "step-shape"
                                        | Some step ->
                                            match memberValue "uses" step, memberValue "run" step with
                                            | None, None -> problem <- Some "step-command-missing"
                                            | Some _, Some _ -> problem <- Some "step-uses-run"
                                            | None, Some run when (stringScalar run
                                                                   |> Option.filter (fun value -> not (String.IsNullOrWhiteSpace value))).IsNone ->
                                                problem <- Some "step-run-shape"
                                            | None, Some _ -> ()
                                            | Some action, None ->
                                                match stringScalar action with
                                                | None -> problem <- Some "step-uses-shape"
                                                | Some value when String.IsNullOrWhiteSpace value ->
                                                    problem <- Some "step-uses-shape"
                                                | Some value when value.Trim().StartsWith(appTokenAction, StringComparison.Ordinal) ->
                                                    if value.Trim().Length = appTokenAction.Length then
                                                        problem <- Some "app-action-ref-missing"
                                                    else
                                                        let inputs = memberValue "with" step
                                                        match inputs with
                                                        | Some value when (mapping value).IsNone ->
                                                            problem <- Some "app-with-shape"
                                                        | _ ->
                                                            match appRequest path (inputs |> Option.bind mapping) with
                                                            | Error diagnostic -> problem <- Some diagnostic.Code
                                                            | Ok(identity, requested) ->
                                                                requests.Add
                                                                    {
                                                                        JobId = jobId
                                                                        StepIndex = index + 1
                                                                        AppIdentitySecret = identity
                                                                        Requested = requested
                                                                    }
                                                | Some _ -> ()
                            | None, Some _ -> problem <- Some "steps-shape"
                match problem with
                | Some code -> error code path
                | None -> Ok { InspectedSteps = inspected; Requests = List.ofSeq requests })
