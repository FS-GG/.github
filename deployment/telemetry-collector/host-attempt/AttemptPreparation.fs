namespace Fs.Gg.Telemetry.HostAttempt

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

type PreparationRequest =
    { PlacementRoot: string
      PlacementSha: string
      PlacementTree: string
      WorkflowPath: string
      WorkflowSha256: string
      QualificationRef: string
      Environment: string
      Repository: string
      Workflow: string
      ReleaseId: int64
      ManifestAssetId: int64
      ArchiveAssetId: int64
      SourceGeneration: int
      RecipeRoot: string
      RecipeSourceSha: string
      RecipeSourceTree: string
      ProfilePath: string
      SourcePinsPath: string
      HostBindingDll: string
      MechanismAdapterPath: string
      Nonce: string
      DestinationId: string
      BudgetSeconds: int }

module AttemptPreparation =
    [<Literal>]
    let RecipeSha = "8ad0da67004d670c6803f34755dfe759a7fc84e7"
    [<Literal>]
    let RecipeTree = "df72335c0ddd892d23ee5d573c10b737245845eb"
    [<Literal>]
    let OperationId = "v2-host-01.8a-native-collaboration-v1"
    [<Literal>]
    let QualificationRef = "qualification/v2-host-native-20260930"
    [<Literal>]
    let Environment = "v2-host-01-8-native-private"
    [<Literal>]
    let Repository = "FS-GG/FS.GG.GitHub.Substrate.Sandbox"
    [<Literal>]
    let Workflow = "v2-host-native-private.yml"
    [<Literal>]
    let ReleaseId = 401047616L
    [<Literal>]
    let ManifestAssetId = 603434428L
    [<Literal>]
    let ArchiveAssetId = 603434427L
    [<Literal>]
    let WorkflowTemplateRelativePath = "deployment/telemetry-collector/private-native-qualification.yml.in"
    let SourcePayloads = ["qualify_native.py";"native_producer_support.py";"native-operation-v1.json";"native-producer-config.toml"]

    let private refuse value = raise (AttemptRefusal value)
    let private require condition value = if not condition then refuse value
    let private hex count (value: string) = not (isNull value) && Regex.IsMatch(value, $@"\A[0-9a-f]{{{count}}}\z", RegexOptions.CultureInvariant)
    let private shaFile path = use stream = File.OpenRead path in Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()

    let private readBounded (stream: Stream) maximumBytes (token: CancellationToken) = task {
        use output = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 4096
        let mutable complete = false
        while not complete do
            let! count = stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
            if count = 0 then complete <- true
            else
                if output.Length + int64 count > int64 maximumBytes then raise (AttemptRefusal "process-output-limit")
                output.Write(buffer,0,count)
        return Encoding.UTF8.GetString(output.ToArray()) }

    let internal runBounded executable workingDirectory arguments (timeoutMilliseconds: int) maximumBytes =
        let stopwatch=Stopwatch.StartNew()
        use timeout = new CancellationTokenSource(timeoutMilliseconds)
        let start = ProcessStartInfo(executable, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory)
        for argument in arguments do start.ArgumentList.Add argument
        use child = new Process(StartInfo = start)
        require (child.Start()) "process-start-refused"
        let outputTask=readBounded child.StandardOutput.BaseStream maximumBytes timeout.Token
        let errorTask=readBounded child.StandardError.BaseStream maximumBytes timeout.Token
        let exitTask=child.WaitForExitAsync(timeout.Token)
        try Task.WhenAll(outputTask :> Task,errorTask :> Task,exitTask).GetAwaiter().GetResult()
        with _ ->
            try child.Kill(true) with _ -> ()
            let remaining=max 0 (timeoutMilliseconds-int stopwatch.ElapsedMilliseconds)
            try child.WaitForExit(remaining) |> ignore with _ -> ()
            if not child.HasExited then refuse "process-settlement-unknown"
            if timeout.IsCancellationRequested then refuse "process-timeout" else refuse "process-output-limit"
        let output,error=outputTask.GetAwaiter().GetResult(),errorTask.GetAwaiter().GetResult()
        child.ExitCode, output, error

    let internal runBoundedInput executable workingDirectory arguments (input:string option) (timeoutMilliseconds:int) maximumBytes =
        let stopwatch=Stopwatch.StartNew()
        use timeout=new CancellationTokenSource(timeoutMilliseconds)
        let start=ProcessStartInfo(executable,UseShellExecute=false,RedirectStandardInput=input.IsSome,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=workingDirectory)
        for argument in arguments do start.ArgumentList.Add argument
        use child=new Process(StartInfo=start)
        require(child.Start()) "process-start-refused"
        let outputTask=readBounded child.StandardOutput.BaseStream maximumBytes timeout.Token
        let errorTask=readBounded child.StandardError.BaseStream 65536 timeout.Token
        let inputTask=task {
            match input with
            | Some value ->
                do! child.StandardInput.WriteAsync(value.AsMemory(),timeout.Token)
                child.StandardInput.Close()
            | None -> () }
        let exitTask=child.WaitForExitAsync(timeout.Token)
        try Task.WhenAll(outputTask :> Task,errorTask :> Task,inputTask :> Task,exitTask).GetAwaiter().GetResult()
        with _ ->
            try child.Kill(true) with _ -> ()
            let remaining=max 0 (timeoutMilliseconds-int stopwatch.ElapsedMilliseconds)
            try child.WaitForExit(remaining)|>ignore with _->()
            if not child.HasExited then refuse "process-settlement-unknown"
            if timeout.IsCancellationRequested then refuse "process-timeout" else refuse "process-io-refused"
        child.ExitCode,outputTask.Result,errorTask.Result

    let private git root args timeoutMilliseconds =
        let code, output, _ = runBounded "/usr/bin/git" root args (min 5000 timeoutMilliseconds) 65536
        require (code = 0) "placement-git-refused"
        output.Trim()

    let private properties (element: JsonElement) =
        require (element.ValueKind = JsonValueKind.Object) "json-object-refused"
        let seen = HashSet<string>(StringComparer.Ordinal)
        [ for property in element.EnumerateObject() do require (seen.Add property.Name) "json-duplicate-key-refused"; yield property.Name, property.Value ] |> Map.ofList
    let private requireKeys expected (fields: Map<string, JsonElement>) = require (Set.ofList expected = Set.ofSeq fields.Keys) "json-fields-refused"
    let private text name (fields: Map<string, JsonElement>) = require (fields[name].ValueKind = JsonValueKind.String) "json-string-refused"; fields[name].GetString()
    let private regularBytes path maximum =
        let info=FileInfo(Path.GetFullPath path)
        require(info.Exists && info.Length>0L && info.Length<=maximum && not(info.Attributes.HasFlag FileAttributes.ReparsePoint)) "binding-input-file-refused"
        let before=info.Length,info.LastWriteTimeUtc
        let bytes=File.ReadAllBytes info.FullName
        info.Refresh();require(before=(info.Length,info.LastWriteTimeUtc)) "binding-input-replaced"
        bytes
    let private shaBytes (bytes:byte array)=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    let private gitBlobSha (bytes:byte array) =
        let header=Encoding.ASCII.GetBytes($"blob {bytes.LongLength}\000")
        Convert.ToHexString(SHA1.HashData(Array.append header bytes)).ToLowerInvariant()
    let private pinnedInventory path expectedSha =
        let bytes=regularBytes path (64L*1024L)
        require(shaBytes bytes=expectedSha) "source-pins-drift"
        use document=JsonDocument.Parse(ReadOnlyMemory<byte>(bytes),JsonDocumentOptions(CommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false,MaxDepth=4))
        let fields=properties document.RootElement
        requireKeys SourcePayloads fields
        SourcePayloads|>List.map(fun name->let value=text name fields in require(hex 64 value) "source-pin-digest-refused";name,value)|>Map.ofList

    let internal runHostBindingAttempt finalRefusal (run:unit -> int*string*string) =
        let code,output,_=run()
        if code=0 then output else refuse finalRefusal

    let private runHostBinding finalRefusal recipeRoot arguments timeoutMilliseconds maximumBytes =
        let clock=Stopwatch.StartNew()
        let remaining()=let value=timeoutMilliseconds-int clock.ElapsedMilliseconds in require(value>0) "host-binding-timeout-refused";value
        runHostBindingAttempt finalRefusal (fun()->runBounded "/usr/bin/dotnet" recipeRoot arguments (remaining()) maximumBytes)

    let private renderBinding request timeoutMilliseconds =
        let args =
            [ request.HostBindingDll; "render"; "--source-root"; request.RecipeRoot; "--source-sha"; request.RecipeSourceSha
              "--profile"; request.ProfilePath; "--source-pins"; request.SourcePinsPath ]
        let output=runHostBinding "host-binding-refused" request.RecipeRoot args (min 90000 timeoutMilliseconds) 65536
        let trimmed = output.TrimEnd('\r', '\n')
        require (not (trimmed.Contains('\n')) && Encoding.UTF8.GetByteCount trimmed <= 16384) "host-binding-output-refused"
        use document = JsonDocument.Parse(trimmed, JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 4))
        let fields = properties document.RootElement
        requireKeys [ "schema"; "recipeSourceSha"; "recipeSourceTree"; "profileSha256"; "operationId"; "sourcePinsSha256"; "producerSha256"; "bindingSha256" ] fields
        require (text "schema" fields = "fsgg.telemetry.host-binding-render/1") "host-binding-schema-refused"
        require (text "recipeSourceSha" fields = RecipeSha && text "recipeSourceTree" fields = RecipeTree) "host-binding-source-refused"
        require (text "operationId" fields = OperationId) "host-binding-operation-refused"
        for name in [ "profileSha256"; "sourcePinsSha256"; "producerSha256"; "bindingSha256" ] do require (hex 64 (text name fields)) "host-binding-digest-refused"
        text "profileSha256" fields, text "sourcePinsSha256" fields, text "producerSha256" fields, text "bindingSha256" fields

    let deriveAdmission (hostBindingDll:string) (recipeRoot:string) (profilePath:string) (sourcePinsPath:string) (nonce:string) timeoutMilliseconds =
        require (Regex.IsMatch(nonce, @"\A[a-z0-9][a-z0-9-]{7,63}\z")) "attempt-identity-refused"
        let args=[hostBindingDll;"derive";"--source-root";recipeRoot;"--source-sha";RecipeSha;"--profile";profilePath;"--source-pins";sourcePinsPath;"--nonce";nonce]
        require (timeoutMilliseconds>0 && timeoutMilliseconds<=90000) "host-binding-timeout-refused"
        let output=runHostBinding "host-binding-derive-refused" recipeRoot args timeoutMilliseconds 4096
        let value=output.TrimEnd('\r','\n')
        require (hex 64 value) "host-binding-admission-refused"
        value

    let prepareWithin producerDllSha request timeoutMilliseconds =
        require(timeoutMilliseconds>0) "preparation-deadline"
        let clock=Stopwatch.StartNew()
        let remaining()=let value=timeoutMilliseconds-int clock.ElapsedMilliseconds in require(value>0) "preparation-deadline";value
        require (request.SourceGeneration = 1 || request.SourceGeneration = 2) "source-generation-refused"
        require (hex 40 request.PlacementSha && hex 40 request.PlacementTree && hex 64 request.WorkflowSha256) "placement-identity-refused"
        require (request.QualificationRef = QualificationRef && request.Environment = Environment && request.Repository = Repository && request.Workflow = Workflow) "route-identity-refused"
        require (request.ReleaseId = ReleaseId && request.ManifestAssetId = ManifestAssetId && request.ArchiveAssetId = ArchiveAssetId) "release-identity-refused"
        require (request.RecipeSourceSha = RecipeSha && request.RecipeSourceTree = RecipeTree) "recipe-identity-refused"
        require (Regex.IsMatch(request.Nonce, @"\A[a-z0-9][a-z0-9-]{7,63}\z") && Regex.IsMatch(request.DestinationId, @"\A[a-z0-9][a-z0-9-]{7,63}\z")) "attempt-identity-refused"
        require (request.BudgetSeconds > 0 && request.BudgetSeconds <= 2700) "budget-refused"
        require (git request.PlacementRoot [ "rev-parse"; "HEAD" ] (remaining()) = request.PlacementSha) "placement-head-drift"
        require (git request.PlacementRoot [ "rev-parse"; request.PlacementSha + "^{tree}" ] (remaining()) = request.PlacementTree) "placement-tree-drift"
        require (git request.PlacementRoot [ "status"; "--porcelain" ] (remaining()) = "") "placement-worktree-dirty"
        require (request.WorkflowPath = ".github/workflows/v2-host-native-private.yml") "workflow-path-refused"
        let workflow = Path.GetFullPath(Path.Combine(request.PlacementRoot, request.WorkflowPath))
        require (File.Exists workflow && shaFile workflow = request.WorkflowSha256) "workflow-drift-refused"
        let profileSha, pinsSha, bindingProducerSha, bindingSha = renderBinding request (remaining())
        require (shaFile request.HostBindingDll=bindingProducerSha) "binding-closure-drift"
        let templatePath=Path.Combine(request.RecipeRoot,WorkflowTemplateRelativePath)
        require (File.Exists templatePath) "workflow-template-missing"
        let template=File.ReadAllText templatePath
        require (template.Split("@@RECIPE_SOURCE_SHA@@",StringSplitOptions.None).Length=3 && template.Split("@@HOST_BINDING_PROFILE_SHA256@@",StringSplitOptions.None).Length=2) "workflow-template-refused"
        let rendered=template.Replace("@@RECIPE_SOURCE_SHA@@",RecipeSha,StringComparison.Ordinal).Replace("@@HOST_BINDING_PROFILE_SHA256@@",profileSha,StringComparison.Ordinal)
        let insertionPoint="      - name: Set up exact HOST binding SDK\n"
        let acquisition="      - name: Acquire exact public-only native inputs before authentication\n        env:\n          GITHUB_TOKEN: ${{ github.token }}\n        run: |\n          set -euo pipefail\n          python3 placement/tools/bounded-public-inputs.py acquire \\\n            --repository \"$GITHUB_REPOSITORY\" \\\n            --output \"$GITHUB_WORKSPACE/placement/_private-inputs\"\n          unset GITHUB_TOKEN\n"
        let index=rendered.IndexOf(insertionPoint,StringComparison.Ordinal)
        require (index>=0 && rendered.IndexOf(insertionPoint,index+1,StringComparison.Ordinal)<0) "workflow-template-anchor-refused"
        require (File.ReadAllText(workflow)=rendered.Insert(index,acquisition)) "workflow-binding-agreement-refused"
        let runtimeHost=Path.GetFullPath("/usr/bin/dotnet")
        require (Path.IsPathFullyQualified request.MechanismAdapterPath) "mechanism-adapter-path-refused"
        let adapter=Path.GetFullPath(request.MechanismAdapterPath)
        let adapterInfo=FileInfo adapter
        require (adapterInfo.Exists && adapterInfo.Length>0L && adapterInfo.Length<=1024L*1024L && not(adapterInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))) "mechanism-adapter-missing"
        { Schema = "fsgg.telemetry.host-attempt-state/2"
          Phase = Phase.Prepared
          Prepared = { SourceGeneration = request.SourceGeneration; PlacementSha = request.PlacementSha; PlacementTree = request.PlacementTree; WorkflowSha256 = request.WorkflowSha256; RecipeSourceSha = RecipeSha; RecipeSourceTree = RecipeTree; ProfileSha256 = profileSha; OperationId = OperationId; BindingSha256 = bindingSha; BindingProducerSha256 = bindingProducerSha; SourcePinsSha256 = pinsSha; ProducerSha256 = producerDllSha; RuntimeHostSha256=shaFile runtimeHost;MechanismAdapterSha256=shaFile adapter; Nonce = request.Nonce; DestinationId = request.DestinationId }
          CurrentSourceGeneration = request.SourceGeneration; CurrentBindingSha256 = bindingSha; ElapsedSeconds = 0; BudgetSeconds = request.BudgetSeconds
          DispatchIntended = false; DispatchMayHaveEffect = false; DispatchAcknowledged = false;EffectCheckFresh=false
          SecretIntentions = Set.empty; SecretAcknowledgments = Set.empty; SecretsMayHaveEffect = Set.empty; SecretAbsenceObserved = Set.empty
          CandidateRuns = []; OwnedRunId = None; CancellationMayHaveEffect = false;RunRetirementObserved=false
          NativeDisposition = NativeDisposition.NativeUnknown; CleanupDisposition = CleanupDisposition.CleanupUnknown; Refusal = None }

    /// Reopens every location-bearing input behind an already authoritative
    /// HostBinding result. This deliberately does not reconstruct the binding
    /// formula or start another binding authority process.
    let revalidateWithin (expected:PreparedIdentity) request timeoutMilliseconds =
        require(timeoutMilliseconds>0) "revalidation-deadline"
        let clock=Stopwatch.StartNew()
        let remaining()=let value=timeoutMilliseconds-int clock.ElapsedMilliseconds in require(value>0) "revalidation-deadline";value
        require(request.SourceGeneration=expected.SourceGeneration && request.PlacementSha=expected.PlacementSha && request.PlacementTree=expected.PlacementTree && request.WorkflowSha256=expected.WorkflowSha256) "revalidation-request-drift"
        require(request.RecipeSourceSha=expected.RecipeSourceSha && request.RecipeSourceTree=expected.RecipeSourceTree && request.Nonce=expected.Nonce && request.DestinationId=expected.DestinationId) "revalidation-request-drift"
        require(request.QualificationRef=QualificationRef && request.Environment=Environment && request.Repository=Repository && request.Workflow=Workflow) "revalidation-route-drift"
        require(request.ReleaseId=ReleaseId && request.ManifestAssetId=ManifestAssetId && request.ArchiveAssetId=ArchiveAssetId) "revalidation-release-drift"
        require(git request.PlacementRoot ["rev-parse";"HEAD"] (remaining())=expected.PlacementSha) "placement-head-drift"
        require(git request.PlacementRoot ["rev-parse";expected.PlacementSha+"^{tree}"] (remaining())=expected.PlacementTree) "placement-tree-drift"
        require(git request.PlacementRoot ["status";"--porcelain"] (remaining())="") "placement-worktree-dirty"
        require(git request.RecipeRoot ["rev-parse";"HEAD"] (remaining())=expected.RecipeSourceSha) "recipe-head-drift"
        require(git request.RecipeRoot ["rev-parse";expected.RecipeSourceSha+"^{tree}"] (remaining())=expected.RecipeSourceTree) "recipe-tree-drift"
        require(git request.RecipeRoot ["status";"--porcelain"] (remaining())="") "recipe-worktree-dirty"
        let workflow=Path.GetFullPath(Path.Combine(request.PlacementRoot,request.WorkflowPath))
        require(request.WorkflowPath=".github/workflows/v2-host-native-private.yml" && File.Exists workflow && shaFile workflow=expected.WorkflowSha256) "workflow-drift-refused"
        require(Path.GetFullPath request.ProfilePath=Path.Combine(Path.GetFullPath request.RecipeRoot,"deployment/telemetry-collector/native-operation-v1.json") && shaFile request.ProfilePath=expected.ProfileSha256) "profile-drift-refused"
        let pins=pinnedInventory request.SourcePinsPath expected.SourcePinsSha256
        for name in SourcePayloads do
            remaining()|>ignore
            let relative="deployment/telemetry-collector/"+name
            let bytes=regularBytes(Path.Combine(request.RecipeRoot,relative))(4L*1024L*1024L)
            require(shaBytes bytes=pins[name]) "source-pin-content-refused"
            require(git request.RecipeRoot ["rev-parse";expected.RecipeSourceSha+":"+relative] (remaining())=gitBlobSha bytes) "source-blob-refused"
        require(shaFile request.HostBindingDll=expected.BindingProducerSha256) "binding-input-drift"
        require(shaFile(Path.GetFullPath "/usr/bin/dotnet")=expected.RuntimeHostSha256 && shaFile request.MechanismAdapterPath=expected.MechanismAdapterSha256) "runtime-closure-drift"
        let templatePath=Path.Combine(request.RecipeRoot,WorkflowTemplateRelativePath)
        require(File.Exists templatePath) "workflow-template-missing"
        let template=File.ReadAllText templatePath
        require(template.Split("@@RECIPE_SOURCE_SHA@@",StringSplitOptions.None).Length=3 && template.Split("@@HOST_BINDING_PROFILE_SHA256@@",StringSplitOptions.None).Length=2) "workflow-template-refused"
        let rendered=template.Replace("@@RECIPE_SOURCE_SHA@@",RecipeSha,StringComparison.Ordinal).Replace("@@HOST_BINDING_PROFILE_SHA256@@",expected.ProfileSha256,StringComparison.Ordinal)
        let insertionPoint="      - name: Set up exact HOST binding SDK\n"
        let acquisition="      - name: Acquire exact public-only native inputs before authentication\n        env:\n          GITHUB_TOKEN: ${{ github.token }}\n        run: |\n          set -euo pipefail\n          python3 placement/tools/bounded-public-inputs.py acquire \\\n            --repository \"$GITHUB_REPOSITORY\" \\\n            --output \"$GITHUB_WORKSPACE/placement/_private-inputs\"\n          unset GITHUB_TOKEN\n"
        let index=rendered.IndexOf(insertionPoint,StringComparison.Ordinal)
        require(index>=0 && rendered.IndexOf(insertionPoint,index+1,StringComparison.Ordinal)<0 && File.ReadAllText(workflow)=rendered.Insert(index,acquisition)) "workflow-binding-agreement-refused"
        remaining() |> ignore

    let prepare producerDllSha request=prepareWithin producerDllSha request 2700000
