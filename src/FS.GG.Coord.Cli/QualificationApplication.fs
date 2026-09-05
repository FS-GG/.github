namespace FS.GG.Coord.Cli

open System
open System.Diagnostics
open System.IO
open System.Reflection
open System.Text
open System.Text.Json
open FS.GG.Coord
open FS.GG.Coord.GitHub

module QualificationApplication =
    [<Literal>]
    let private ExecutionSchema = "fsgg.qualification.execution/1"

    type private ToolResolver = { Id: string; Path: string; VersionArguments: string list }
    type private OperationResolver = { Id: string; Arguments: string list; Artifacts: string list }
    type private ExecutorResolver = { Id: string; Role: string }
    type private FixtureResolver =
        { MutationId: string; ExecutorId: string; ExecutorRole: string; Path: string; Arguments: string list }
    type private Execution =
        { Checkout: string; Environment: (string * string) list; TimeoutSeconds: int
          Executor: ExecutorResolver; Tools: ToolResolver list; Operations: OperationResolver list
          Fixtures: FixtureResolver list; HostedObservationPaths: string list; ObligationCommentPaths: string list }
    type private ProcessResult = { ExitCode: int; Stdout: string; Stderr: string }

    let private strictObject (label: string) (expected: string list) (element: JsonElement) =
        if element.ValueKind <> JsonValueKind.Object then raise (FormatException($"%s{label} must be an object"))
        let expectedSet = Set.ofList expected
        let observed = element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq
        let missing = Set.difference expectedSet observed |> Set.toList
        let unknown = Set.difference observed expectedSet |> Set.toList
        let missingText = String.concat "," missing
        let unknownText = String.concat "," unknown
        if not missing.IsEmpty then raise (FormatException($"%s{label} is missing fields: %s{missingText}"))
        if not unknown.IsEmpty then raise (FormatException($"%s{label} has unknown fields: %s{unknownText}"))
    let private text (label: string) (name: string) (element: JsonElement) =
        let value = element.GetProperty name
        if value.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()) then
            raise (FormatException($"%s{label}.%s{name} must be a non-empty string"))
        value.GetString()
    let private strings (label: string) (name: string) (element: JsonElement) =
        let value = element.GetProperty name
        if value.ValueKind <> JsonValueKind.Array then raise (FormatException($"%s{label}.%s{name} must be an array"))
        value.EnumerateArray()
        |> Seq.map (fun item ->
            if item.ValueKind <> JsonValueKind.String || String.IsNullOrWhiteSpace(item.GetString()) then
                raise (FormatException($"%s{label}.%s{name} must contain non-empty strings"))
            item.GetString())
        |> List.ofSeq
    let private array (label: string) (name: string) (element: JsonElement) =
        let value = element.GetProperty name
        if value.ValueKind <> JsonValueKind.Array then raise (FormatException($"%s{label}.%s{name} must be an array"))
        value.EnumerateArray() |> List.ofSeq

    let private parseExecution (path: string) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory(File.ReadAllBytes path))
            let root = document.RootElement
            strictObject "execution"
                [ "schema"; "checkout"; "environment"; "timeoutSeconds"; "executor"; "tools"; "operations"; "fixtures"
                  "hostedObservationPaths"; "obligationCommentPaths" ] root
            if text "execution" "schema" root <> ExecutionSchema then raise (FormatException($"schema must be '%s{ExecutionSchema}'"))
            let timeout = root.GetProperty("timeoutSeconds").GetInt32()
            if timeout < 1 || timeout > 3600 then raise (FormatException("timeoutSeconds must be between 1 and 3600"))
            let environment =
                array "execution" "environment" root
                |> List.mapi (fun index item ->
                    let label = $"environment[%d{index}]"
                    strictObject label [ "name"; "value" ] item
                    text label "name" item, text label "value" item)
            let executorElement = root.GetProperty "executor"
            strictObject "executor" [ "id"; "role" ] executorElement
            let executor =
                { Id = text "executor" "id" executorElement
                  Role = text "executor" "role" executorElement }
            let tools =
                array "execution" "tools" root
                |> List.mapi (fun index item ->
                    let label = $"tools[%d{index}]"
                    strictObject label [ "id"; "path"; "versionArguments" ] item
                    { Id = text label "id" item; Path = text label "path" item; VersionArguments = strings label "versionArguments" item })
            let operations =
                array "execution" "operations" root
                |> List.mapi (fun index item ->
                    let label = $"operations[%d{index}]"
                    strictObject label [ "id"; "arguments"; "artifacts" ] item
                    { Id = text label "id" item; Arguments = strings label "arguments" item; Artifacts = strings label "artifacts" item })
            let fixtures =
                array "execution" "fixtures" root
                |> List.mapi (fun index item ->
                    let label = $"fixtures[%d{index}]"
                    strictObject label [ "mutationId"; "executorId"; "executorRole"; "path"; "arguments" ] item
                    { MutationId = text label "mutationId" item; ExecutorId = text label "executorId" item
                      ExecutorRole = text label "executorRole" item
                      Path = text label "path" item; Arguments = strings label "arguments" item })
            Ok { Checkout = text "execution" "checkout" root; Environment = environment; TimeoutSeconds = timeout
                 Executor = executor; Tools = tools; Operations = operations; Fixtures = fixtures
                 HostedObservationPaths = strings "execution" "hostedObservationPaths" root
                 ObligationCommentPaths = strings "execution" "obligationCommentPaths" root }
        with
        | :? JsonException as error -> Error [ $"invalid execution JSON: %s{error.Message}" ]
        | :? FormatException as error -> Error [ error.Message ]
        | :? IOException as error -> Error [ error.Message ]
        | error -> Error [ $"invalid execution input: %s{error.Message}" ]

    let private duplicates (label: string) (values: string list) =
        values |> List.countBy id |> List.choose (fun (value, count) -> if count > 1 then Some($"duplicate %s{label} identity '%s{value}'") else None)

    let private runProcess (timeoutSeconds: int) (checkout: string) (environment: (string * string) list) (executable: string) (arguments: string list) =
        try
            let info = ProcessStartInfo(executable)
            info.WorkingDirectory <- checkout
            info.UseShellExecute <- false
            info.RedirectStandardOutput <- true
            info.RedirectStandardError <- true
            info.CreateNoWindow <- true
            info.Environment.Clear()
            for name, value in environment do info.Environment[name] <- value
            for argument in arguments do info.ArgumentList.Add argument
            use childProcess = new Process(StartInfo = info)
            if not (childProcess.Start()) then Error "process did not start" else
            let stdout = childProcess.StandardOutput.ReadToEndAsync()
            let stderr = childProcess.StandardError.ReadToEndAsync()
            if not (childProcess.WaitForExit(timeoutSeconds * 1000)) then
                childProcess.Kill(true)
                childProcess.WaitForExit()
                Error $"process exceeded %d{timeoutSeconds}s timeout"
            else
                Ok { ExitCode = childProcess.ExitCode; Stdout = stdout.GetAwaiter().GetResult(); Stderr = stderr.GetAwaiter().GetResult() }
        with error -> Error error.Message

    let private frame (value: string) = $"%d{Encoding.UTF8.GetByteCount value}:%s{value}"
    let private digestText (values: string list) = values |> List.map frame |> String.concat "|" |> Encoding.UTF8.GetBytes |> CanonicalJson.sha256
    let private digestFile (path: string) = File.ReadAllBytes path |> CanonicalJson.sha256
    let private fullPath (path: string) = Path.GetFullPath path
    let private resolvePath (root: string) (path: string) =
        if Path.IsPathFullyQualified path then fullPath path else Path.GetFullPath(path, root)
    let private under (root: string) (path: string) =
        let rootPath = fullPath root
        let candidate = Path.GetFullPath(path, rootPath)
        let prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + string Path.DirectorySeparatorChar
        if candidate = rootPath || candidate.StartsWith(prefix, StringComparison.Ordinal) then Ok candidate
        else Error $"artifact path escapes checkout: %s{path}"

    let run inputPath executionPath =
        match Qualification.parseInput (File.ReadAllBytes inputPath), parseExecution executionPath with
        | Error errors, _ | _, Error errors -> Error errors
        | Ok input, Ok execution ->
            let errors = ResizeArray<string>()
            let checkout = fullPath execution.Checkout
            if not (Directory.Exists checkout) then errors.Add($"checkout does not exist: %s{checkout}")
            if checkout = fullPath (Directory.GetCurrentDirectory()) then errors.Add "qualification checkout must be isolated from the invoking checkout"
            errors.AddRange(duplicates "environment" (execution.Environment |> List.map fst))
            errors.AddRange(duplicates "tool" (execution.Tools |> List.map _.Id))
            errors.AddRange(duplicates "operation" (execution.Operations |> List.map _.Id))
            errors.AddRange(duplicates "fixture mutation" (execution.Fixtures |> List.map _.MutationId))
            let hostedObservationPaths =
                execution.HostedObservationPaths
                |> List.map (fun path -> under checkout path |> Result.defaultValue (resolvePath checkout path))
            errors.AddRange(duplicates "hosted observation path" hostedObservationPaths)
            let manifestToolIds = input.ToolManifest |> List.map _.Id |> Set.ofList
            let resolverToolIds = execution.Tools |> List.map _.Id |> Set.ofList
            if manifestToolIds <> resolverToolIds then errors.Add "execution tools do not exactly match the closed tool manifest"
            let inputOperationIds = input.Operations |> List.map _.Id |> Set.ofList
            let resolverOperationIds = execution.Operations |> List.map _.Id |> Set.ofList
            if inputOperationIds <> resolverOperationIds then errors.Add "execution operations do not exactly match the qualification input"
            for operation in input.Operations do
                if not (Set.contains operation.Tool.Id manifestToolIds) then errors.Add($"operation '%s{operation.Id}' names undeclared tool '%s{operation.Tool.Id}'")
                if operation.Executor.Id <> input.Executor.Id || operation.Executor.Role <> input.Executor.Role then
                    errors.Add($"operation '%s{operation.Id}' names the wrong executor")
            let inputMutationIds = input.Mutations |> List.map _.Id |> Set.ofList
            let resolverMutationIds = execution.Fixtures |> List.map _.MutationId |> Set.ofList
            if inputMutationIds <> resolverMutationIds then errors.Add "execution fixtures do not exactly match the qualification mutations"
            if execution.HostedObservationPaths.Length < 2 then errors.Add "at least two hosted observation paths are required"
            if execution.Executor.Id <> input.Executor.Id || execution.Executor.Role <> input.Executor.Role then
                errors.Add "execution executor does not match the qualification executor identity"
            let executorPath = Assembly.GetExecutingAssembly().Location
            if String.IsNullOrWhiteSpace executorPath || not (File.Exists executorPath) then errors.Add "executing qualification assembly identity is unavailable"
            if errors.Count > 0 then Error(List.ofSeq errors) else
            let resolvedExecutor = { input.Executor with ImplementationSha256 = digestFile executorPath }
            let resolvers = execution.Tools |> List.map (fun tool -> tool.Id, tool) |> Map.ofList
            let operationResolvers = execution.Operations |> List.map (fun operation -> operation.Id, operation) |> Map.ofList
            let fixtureResolvers = execution.Fixtures |> List.map (fun fixture -> fixture.MutationId, fixture) |> Map.ofList
            let resolvedTools = ResizeArray<string * Qualification.ToolIdentity * string>()
            for declared in input.ToolManifest do
                let resolver = resolvers[declared.Id]
                let path = resolvePath checkout resolver.Path
                if not (File.Exists path) then errors.Add($"tool '%s{declared.Id}' does not exist") else
                let actualDigest = digestFile path
                match runProcess execution.TimeoutSeconds checkout execution.Environment path resolver.VersionArguments with
                | Error reason -> errors.Add($"tool '%s{declared.Id}' version probe failed: %s{reason}")
                | Ok result when result.ExitCode <> 0 -> errors.Add($"tool '%s{declared.Id}' version probe exited %d{result.ExitCode}")
                | Ok result ->
                    let actual = { declared with Version = result.Stdout.Trim(); Sha256 = actualDigest }
                    resolvedTools.Add(declared.Id, actual, path)
            if errors.Count > 0 then Error(List.ofSeq errors) else
            let toolMap = resolvedTools |> Seq.map (fun (id, identity, path) -> id, (identity, path)) |> Map.ofSeq
            let checkoutFacts =
                match toolMap.TryFind "git" with
                | None -> Error [ "closed tool manifest must include tool id 'git' for checkout identity" ]
                | Some(_, git) ->
                    match runProcess execution.TimeoutSeconds checkout execution.Environment git [ "rev-parse"; "HEAD" ],
                          runProcess execution.TimeoutSeconds checkout execution.Environment git [ "status"; "--porcelain"; "--untracked-files=all" ] with
                    | Ok head, Ok status when head.ExitCode = 0 && status.ExitCode = 0 -> Ok(head.Stdout.Trim(), String.IsNullOrEmpty status.Stdout)
                    | values -> Error [ $"could not establish checkout identity: %A{values}" ]
            match checkoutFacts with
            | Error values -> Error values
            | Ok(head, _) when head <> input.SubjectRevision ->
                Error [ $"checkout HEAD '%s{head}' does not match subject revision '%s{input.SubjectRevision}'" ]
            | Ok(_, initiallyClean) ->
            let usedArtifacts = Collections.Generic.HashSet<string>(StringComparer.Ordinal)
            let observed = ResizeArray<Qualification.OperationEvidence>()
            for template in input.Operations do
                let resolver = operationResolvers[template.Id]
                let actualTool, executable = toolMap[template.Tool.Id]
                let execute () = runProcess execution.TimeoutSeconds checkout execution.Environment executable resolver.Arguments
                let operationErrorCount = errors.Count
                let artifactPaths =
                    resolver.Artifacts
                    |> List.choose (fun artifact ->
                        match under checkout artifact with
                        | Error reason -> errors.Add reason; None
                        | Ok path when not (usedArtifacts.Add path) -> errors.Add($"artifact is reused by unrelated operations: %s{artifact}"); None
                        | Ok path when File.Exists path -> errors.Add($"operation '%s{template.Id}' artifact exists before execution: %s{artifact}"); None
                        | Ok path -> Some(artifact, path))
                let executionResult =
                    if errors.Count <> operationErrorCount then Error "artifact preflight failed"
                    else execute ()
                match executionResult with
                | Error reason -> errors.Add($"operation '%s{template.Id}' failed to execute: %s{reason}")
                | Ok result ->
                    let artifactDigests = ResizeArray<string>()
                    for artifact, path in artifactPaths do
                        if not (File.Exists path) then errors.Add($"operation '%s{template.Id}' artifact does not exist: %s{artifact}")
                        else artifactDigests.Add(digestFile path)
                    let resultDigest = digestText [ string result.ExitCode; result.Stdout; result.Stderr ]
                    let replay =
                        if template.Kind <> Qualification.FixedPoint then None else
                        match execute () with
                        | Error reason -> errors.Add($"fixed-point replay '%s{template.Id}' failed to execute: %s{reason}"); None
                        | Ok replayResult ->
                            let replayArtifactDigests = artifactPaths |> List.map (snd >> digestFile)
                            if replayArtifactDigests <> List.ofSeq artifactDigests then
                                errors.Add($"fixed-point replay '%s{template.Id}' changed artifact bytes")
                            Some(digestText [ string replayResult.ExitCode; replayResult.Stdout; replayResult.Stderr ])
                    observed.Add
                        { template with
                            Tool = actualTool
                            Executor = resolvedExecutor
                            CommandSha256 = digestText (template.Tool.Id :: resolver.Arguments)
                            ArtifactSha256 = List.ofSeq artifactDigests
                            ResultSha256 = resultDigest
                            ReplayResultSha256 = replay
                            ExitCode = result.ExitCode
                            Refusal = if result.ExitCode = 0 then None else Some(result.Stderr.Trim()) }
            if errors.Count > 0 then Error(List.ofSeq errors) else
            let observedById = observed |> Seq.map (fun operation -> operation.Id, operation) |> Map.ofSeq
            let mutations =
                input.Mutations
                |> List.map (fun mutation ->
                    let fixture = fixtureResolvers[mutation.Id]
                    let fixturePath = resolvePath checkout fixture.Path
                    if fixture.ExecutorId = resolvedExecutor.Id then errors.Add($"mutation fixture '%s{mutation.Id}' reuses the production executor identity")
                    if fixture.ExecutorRole = resolvedExecutor.Role then errors.Add($"mutation fixture '%s{mutation.Id}' reuses the production executor role")
                    if not (File.Exists fixturePath) then errors.Add($"mutation fixture '%s{mutation.Id}' implementation does not exist")
                    let fixtureDigest = if File.Exists fixturePath then digestFile fixturePath else mutation.FixtureImplementationSha256
                    if fixtureDigest = resolvedExecutor.ImplementationSha256 then errors.Add($"mutation fixture '%s{mutation.Id}' reuses the production implementation")
                    match runProcess execution.TimeoutSeconds checkout execution.Environment fixturePath fixture.Arguments with
                    | Error reason -> errors.Add($"mutation fixture '%s{mutation.Id}' failed to execute: %s{reason}")
                    | Ok result when result.ExitCode = 0 -> errors.Add($"mutation fixture '%s{mutation.Id}' did not refuse")
                    | Ok result when result.Stderr.Trim() <> mutation.ExpectedRefusal -> errors.Add($"mutation fixture '%s{mutation.Id}' refusal did not match exactly")
                    | Ok _ -> ()
                    match observedById.TryFind mutation.OperationId with
                    | Some operation ->
                        { mutation with ObservedRefusal = operation.Refusal |> Option.defaultValue ""
                                        ProductionImplementationSha256 = resolvedExecutor.ImplementationSha256
                                        FixtureImplementationSha256 = fixtureDigest
                                        FixtureExecutorId = fixture.ExecutorId
                                        FixtureExecutorRole = fixture.ExecutorRole }
                    | None -> mutation)
            if errors.Count > 0 then Error(List.ofSeq errors) else
            let hostedArtifacts =
                input.Operations
                |> List.filter (fun operation -> operation.Kind = Qualification.Hosted)
                |> List.collect (fun operation -> operationResolvers[operation.Id].Artifacts)
                |> List.map (under checkout)
                |> List.choose (function Ok path -> Some path | _ -> None)
                |> Set.ofList
            let hosted =
                execution.HostedObservationPaths
                |> List.map (fun path ->
                    match under checkout path with
                    | Error reason -> errors.Add reason; None
                    | Ok resolved when not (Set.contains resolved hostedArtifacts) -> errors.Add($"hosted observation is not an artifact of a hosted operation: %s{path}"); None
                    | Ok resolved when not (File.Exists resolved) -> errors.Add($"hosted observation does not exist: %s{path}"); None
                    | Ok resolved ->
                        match QualificationEvidence.parseHostedSnapshot (File.ReadAllBytes resolved) with
                        | Error reasons -> errors.AddRange reasons; None
                        | Ok snapshot -> Some(QualificationEvidence.observeHosted snapshot))
                |> List.choose id
            let obligationComments =
                execution.ObligationCommentPaths
                |> List.choose (fun path ->
                    match under checkout path with
                    | Error reason -> errors.Add reason; None
                    | Ok resolved when not (Set.contains resolved hostedArtifacts) -> errors.Add($"obligation comment is not an artifact of a hosted operation: %s{path}"); None
                    | Ok resolved when not (File.Exists resolved) -> errors.Add($"obligation comment does not exist: %s{path}"); None
                    | Ok resolved ->
                        match QualificationEvidence.parseObligationReadback (File.ReadAllBytes resolved) with
                        | Error reasons -> errors.AddRange reasons; None
                        | Ok comment -> Some comment)
            let reviewComments =
                obligationComments
                |> List.map (fun comment ->
                    ({ Id = comment.CommentId; Url = comment.Url; Body = comment.Body }: Driver.ReviewComment))
            let obligations =
                DeliveryApplication.obligationsFromComments input.SubjectRevision reviewComments
                |> Result.mapError List.singleton
                |> Result.map (fun declarations ->
                    ({ HeadSha = input.SubjectRevision;
                      Declarations =
                        (if declarations.IsEmpty then [ Qualification.NoObligations ]
                         else declarations |> List.map (fun item -> Qualification.Obligation { Id = item.Id; Kind = item.Kind }));
                      Readbacks =
                        obligationComments
                        |> List.map (fun (comment: QualificationEvidence.ObligationComment) ->
                            ({ CommentId = comment.CommentId; Url = comment.Url; Author = comment.Author }
                             : Qualification.ObligationAuthority)) }
                     : Qualification.ObligationObservation))
            match obligations with Error reasons -> errors.AddRange reasons | Ok _ -> ()
            if errors.Count > 0 then Error(List.ofSeq errors) else
            let qualified =
                { input with CheckoutClean = initiallyClean; Executor = resolvedExecutor
                             Operations = List.ofSeq observed; Mutations = mutations; HostedObservations = hosted
                             Obligations = obligations |> Result.defaultValue input.Obligations }
            Qualification.validate qualified |> Result.mapError (List.map string)

    // Production acceptance uses this route so the caller-authored execution resolver cannot
    // substitute the repository identity. The ordinary `run` entry point remains useful for
    // isolated qualification authoring and tests; only this adapter seals roadmap acceptance.
    let runBoundToTree expectedTree inputPath executionPath =
        match parseExecution executionPath with
        | Error errors -> Error errors
        | Ok execution ->
            let errors = ResizeArray<string>()
            let checkout = fullPath execution.Checkout
            if execution.Environment |> List.exists (fun (name, _) -> name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)) then
                errors.Add "production qualification environment may not override GIT_* authority"
            for tool in execution.Tools do
                let resolved = resolvePath checkout tool.Path
                let trustedExternal =
                    resolved = "/usr/bin/git"
                    || resolved = "/usr/bin/dotnet"
                    || resolved = "/usr/share/dotnet/dotnet"
                    || resolved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "fsgg-sdd")
                if tool.Id = "git" && resolved <> "/usr/bin/git" then
                    errors.Add "production qualification must resolve tool id 'git' to /usr/bin/git"
                elif not trustedExternal && (under checkout resolved |> Result.isError) then
                    errors.Add($"production qualification tool '%s{tool.Id}' escapes the candidate checkout")
            for fixture in execution.Fixtures do
                if under checkout fixture.Path |> Result.isError then
                    errors.Add($"production qualification fixture '%s{fixture.MutationId}' escapes the candidate checkout")
            if not (File.Exists "/usr/bin/git") then errors.Add "pinned system git /usr/bin/git is unavailable"
            elif Directory.Exists checkout then
                let trustedEnvironment =
                    [ "PATH", "/usr/bin:/bin"
                      "HOME", checkout
                      "GIT_CONFIG_NOSYSTEM", "1"
                      "GIT_CONFIG_GLOBAL", "/dev/null"
                      "GIT_NO_REPLACE_OBJECTS", "1" ]
                match runProcess execution.TimeoutSeconds checkout trustedEnvironment "/usr/bin/git" [ "rev-parse"; "HEAD^{tree}" ],
                      runProcess execution.TimeoutSeconds checkout trustedEnvironment "/usr/bin/git" [ "status"; "--porcelain"; "--untracked-files=all" ] with
                | Ok tree, Ok status when tree.ExitCode = 0 && status.ExitCode = 0 ->
                    if tree.Stdout.Trim() <> expectedTree then errors.Add($"trusted checkout tree '%s{tree.Stdout.Trim()}' does not match expected candidate tree '%s{expectedTree}'")
                    if not (String.IsNullOrEmpty status.Stdout) then errors.Add "trusted candidate checkout is not clean"
                | values -> errors.Add($"trusted git could not establish production checkout identity: %A{values}")
            else errors.Add($"checkout does not exist: %s{checkout}")
            if errors.Count > 0 then Error(List.ofSeq errors) else run inputPath executionPath
