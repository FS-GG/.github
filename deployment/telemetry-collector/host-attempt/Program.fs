open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.Win32.SafeHandles
open System.Runtime.InteropServices
open System.Threading
open Fs.Gg.Telemetry.HostAttempt

let refuse value = raise (AttemptRefusal value)
let require condition value = if not condition then refuse value

let args (values: string array) =
    require (values.Length % 2 = 0) "arguments-refused"
    let pairs = values |> Array.chunkBySize 2
    require (pairs |> Array.forall (fun pair -> pair[0].StartsWith("--", StringComparison.Ordinal) && pair[0].Length > 2)) "arguments-refused"
    require (pairs |> Array.map (fun pair -> pair[0]) |> Array.distinct |> Array.length = pairs.Length) "arguments-refused"
    pairs |> Array.map (fun pair -> pair[0].Substring(2), pair[1]) |> Map.ofArray

let exactArgs (expected: string list) (actual: Map<string,string>) = require (Set.ofList expected = Set.ofSeq actual.Keys) "arguments-refused"
let required name (values: Map<string,string>) = values.TryFind name |> Option.filter (String.IsNullOrEmpty >> not) |> Option.defaultWith (fun () -> refuse ("argument-missing:" + name))

module Wire =
    let read path maximum =
        let info = FileInfo path
        require (info.Exists && info.Length > 0L && info.Length <= maximum && not (info.Attributes.HasFlag FileAttributes.ReparsePoint)) "input-refused"
        File.ReadAllBytes path

    let document bytes = JsonDocument.Parse(ReadOnlyMemory<byte>(bytes), JsonDocumentOptions(CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 16))
    let fields (element: JsonElement) =
        require (element.ValueKind = JsonValueKind.Object) "json-object-refused"
        let seen = HashSet<string>(StringComparer.Ordinal)
        [ for property in element.EnumerateObject() do require (seen.Add property.Name) "json-duplicate-key-refused"; yield property.Name, property.Value ] |> Map.ofList
    let keys (expected: string list) (values: Map<string,JsonElement>) = require (Set.ofList expected = Set.ofSeq values.Keys) "json-fields-refused"
    let text name (values: Map<string,JsonElement>) = require (values[name].ValueKind = JsonValueKind.String) "json-string-refused"; values[name].GetString()
    let integer name (values: Map<string,JsonElement>) = let mutable result = 0 in require (values[name].ValueKind = JsonValueKind.Number && values[name].TryGetInt32(&result)) "json-integer-refused"; result
    let integer64 name (values: Map<string,JsonElement>) = let mutable result = 0L in require (values[name].ValueKind = JsonValueKind.Number && values[name].TryGetInt64(&result)) "json-integer-refused"; result
    let boolean name (values: Map<string,JsonElement>) = require (values[name].ValueKind = JsonValueKind.True || values[name].ValueKind = JsonValueKind.False) "json-boolean-refused"; values[name].GetBoolean()

    let private phase = function
        | "unprepared" -> Phase.Unprepared | "prepared" -> Phase.Prepared | "placement-observed" -> Phase.PlacementObserved
        | "secret-placement-pending" -> Phase.SecretPlacementPending | "secrets-observed" -> Phase.SecretsObserved
        | "dispatch-pending" -> Phase.DispatchPending | "discovery" -> Phase.Discovery | "watching" -> Phase.Watching
        | "cleanup-pending" -> Phase.CleanupPending | "finalized" -> Phase.Finalized | "refused" -> Phase.Refused
        | "unknown" -> Phase.Unknown | _ -> refuse "phase-refused"
    let private native = function "unknown" -> NativeDisposition.NativeUnknown | "accepted" -> NativeDisposition.NativeAccepted | "refused" -> NativeDisposition.NativeRefused | _ -> refuse "native-disposition-refused"
    let private cleanup = function "unknown" -> CleanupDisposition.CleanupUnknown | "pending" -> CleanupDisposition.CleanupPending | "secrets-absent" -> CleanupDisposition.SecretsAbsent | _ -> refuse "cleanup-disposition-refused"

    let preparationElement element =
        let v = fields element
        keys [ "schema"; "placementRoot"; "placementSha"; "placementTree"; "workflowPath"; "workflowSha256"; "qualificationRef"; "environment"; "repository"; "workflow"; "releaseId"; "manifestAssetId"; "archiveAssetId"; "sourceGeneration"; "recipeRoot"; "recipeSourceSha"; "recipeSourceTree"; "profilePath"; "sourcePinsPath"; "hostBindingDll"; "mechanismAdapterPath"; "nonce"; "destinationId"; "budgetSeconds" ] v
        require (text "schema" v = "fsgg.telemetry.host-attempt-preparation/1") "preparation-schema-refused"
        { PlacementRoot=text "placementRoot" v; PlacementSha=text "placementSha" v; PlacementTree=text "placementTree" v; WorkflowPath=text "workflowPath" v; WorkflowSha256=text "workflowSha256" v
          QualificationRef=text "qualificationRef" v; Environment=text "environment" v; Repository=text "repository" v; Workflow=text "workflow" v
          ReleaseId=integer64 "releaseId" v; ManifestAssetId=integer64 "manifestAssetId" v; ArchiveAssetId=integer64 "archiveAssetId" v; SourceGeneration=integer "sourceGeneration" v
          RecipeRoot=text "recipeRoot" v; RecipeSourceSha=text "recipeSourceSha" v; RecipeSourceTree=text "recipeSourceTree" v; ProfilePath=text "profilePath" v
          SourcePinsPath=text "sourcePinsPath" v; HostBindingDll=text "hostBindingDll" v; MechanismAdapterPath=text "mechanismAdapterPath" v; Nonce=text "nonce" v; DestinationId=text "destinationId" v; BudgetSeconds=integer "budgetSeconds" v }

    let preparation bytes =
        use doc = document bytes
        preparationElement doc.RootElement

    let runRequest bytes =
        use doc=document bytes
        let v=fields doc.RootElement
        keys ["schema";"preparation";"producerSha256";"transportSha256";"invocationId";"leaseId";"authChannel";"ownershipChannel";"verifierChannel"] v
        require(text "schema" v="fsgg.telemetry.host-attempt-run-request/1") "run-request-schema-refused"
        let result={Preparation=preparationElement v["preparation"];ProducerSha256=text "producerSha256" v;TransportSha256=text "transportSha256" v;InvocationId=text "invocationId" v;LeaseId=text "leaseId" v;AuthChannel=text "authChannel" v;OwnershipChannel=text "ownershipChannel" v;VerifierChannel=text "verifierChannel" v}
        require(Regex.IsMatch(result.ProducerSha256,"\\A[0-9a-f]{64}\\z")&&Regex.IsMatch(result.TransportSha256,"\\A[0-9a-f]{64}\\z")&&Regex.IsMatch(result.InvocationId,"\\A[a-z0-9][a-z0-9-]{7,63}\\z")&&Regex.IsMatch(result.LeaseId,"\\A[a-z0-9][a-z0-9-]{7,63}\\z")) "run-request-identity-refused"
        require(result.AuthChannel="auth-metadata-v1"&&result.OwnershipChannel="owned-run-v1"&&result.VerifierChannel="native-verifier-v1") "run-request-channel-refused"
        result

    let private secretSet name (v: Map<string,JsonElement>) =
        require (v[name].ValueKind = JsonValueKind.Array) "json-array-refused"
        let values = [ for item in v[name].EnumerateArray() do require (item.ValueKind = JsonValueKind.String) "json-array-item-refused"; yield ClosedNames.secretOf(item.GetString()) ]
        require (values.Length = (values |> List.distinct |> List.length)) "json-array-duplicate-refused"; Set.ofList values
    let private strings name (v: Map<string,JsonElement>) maximum =
        require (v[name].ValueKind = JsonValueKind.Array) "json-array-refused"
        let values = [ for item in v[name].EnumerateArray() do require (item.ValueKind = JsonValueKind.String) "json-array-item-refused"; let value=item.GetString() in require (value.Length>0 && value.Length<=maximum) "json-array-item-refused"; yield value ]
        require (values.Length = (values |> List.distinct |> List.length)) "json-array-duplicate-refused"; values

    let state bytes =
        use doc = document bytes
        let v=fields doc.RootElement
        keys [ "schema";"phase";"sourceGeneration";"placementSha";"placementTree";"workflowSha256";"recipeSourceSha";"recipeSourceTree";"profileSha256";"operationId";"bindingSha256";"bindingProducerSha256";"sourcePinsSha256";"producerSha256";"runtimeHostSha256";"mechanismAdapterSha256";"nonce";"destinationId";"currentSourceGeneration";"currentBindingSha256";"elapsedSeconds";"budgetSeconds";"dispatchIntended";"dispatchMayHaveEffect";"dispatchAcknowledged";"effectCheckFresh";"secretIntentions";"secretAcknowledgments";"secretsMayHaveEffect";"secretAbsenceObserved";"candidateRuns";"ownedRunId";"cancellationMayHaveEffect";"runRetirementObserved";"nativeDisposition";"cleanupDisposition";"refusal" ] v
        require (text "schema" v = "fsgg.telemetry.host-attempt-state/2") "state-schema-refused"
        let optionalText name = if v[name].ValueKind=JsonValueKind.Null then None else Some(text name v)
        { Schema=text "schema" v; Phase=phase(text "phase" v)
          Prepared={ SourceGeneration=integer "sourceGeneration" v; PlacementSha=text "placementSha" v; PlacementTree=text "placementTree" v; WorkflowSha256=text "workflowSha256" v; RecipeSourceSha=text "recipeSourceSha" v; RecipeSourceTree=text "recipeSourceTree" v; ProfileSha256=text "profileSha256" v; OperationId=text "operationId" v; BindingSha256=text "bindingSha256" v; BindingProducerSha256=text "bindingProducerSha256" v; SourcePinsSha256=text "sourcePinsSha256" v; ProducerSha256=text "producerSha256" v;RuntimeHostSha256=text "runtimeHostSha256" v;MechanismAdapterSha256=text "mechanismAdapterSha256" v; Nonce=text "nonce" v; DestinationId=text "destinationId" v }
          CurrentSourceGeneration=integer "currentSourceGeneration" v; CurrentBindingSha256=text "currentBindingSha256" v; ElapsedSeconds=integer "elapsedSeconds" v; BudgetSeconds=integer "budgetSeconds" v
          DispatchIntended=boolean "dispatchIntended" v; DispatchMayHaveEffect=boolean "dispatchMayHaveEffect" v; DispatchAcknowledged=boolean "dispatchAcknowledged" v;EffectCheckFresh=boolean "effectCheckFresh" v
          SecretIntentions=secretSet "secretIntentions" v; SecretAcknowledgments=secretSet "secretAcknowledgments" v; SecretsMayHaveEffect=secretSet "secretsMayHaveEffect" v; SecretAbsenceObserved=secretSet "secretAbsenceObserved" v
          CandidateRuns=strings "candidateRuns" v 32; OwnedRunId=optionalText "ownedRunId"; CancellationMayHaveEffect=boolean "cancellationMayHaveEffect" v;RunRetirementObserved=boolean "runRetirementObserved" v
          NativeDisposition=native(text "nativeDisposition" v); CleanupDisposition=cleanup(text "cleanupDisposition" v); Refusal=optionalText "refusal" }

    let private action element =
        let v=fields element
        keys ["name";"argument"] v
        let argument=if v["argument"].ValueKind=JsonValueKind.Null then None else Some(text "argument" v)
        ClosedNames.actionOf (text "name" v) argument

    let observation bytes =
        use doc=document bytes
        let v=fields doc.RootElement
        require (v.ContainsKey "schema" && text "schema" v="fsgg.telemetry.host-attempt-observation/1" && v.ContainsKey "kind") "observation-schema-refused"
        match text "kind" v with
        | "source-revalidated" -> keys ["schema";"kind";"generation";"bindingSha256";"profileSha256";"workflowSha256"] v; Observation.SourceRevalidated(integer "generation" v,text "bindingSha256" v,text "profileSha256" v,text "workflowSha256" v)
        | "placement-observed" -> keys ["schema";"kind"] v; Observation.PlacementObserved
        | "effect-check-observed" ->
            keys ["schema";"kind";"sourceGeneration";"bindingSha256";"profileSha256";"workflowSha256";"bindingProducerSha256";"runtimeHostSha256";"mechanismAdapterSha256"] v
            Observation.EffectCheckObserved {SourceGeneration=integer "sourceGeneration" v;BindingSha256=text "bindingSha256" v;ProfileSha256=text "profileSha256" v;WorkflowSha256=text "workflowSha256" v;BindingProducerSha256=text "bindingProducerSha256" v;RuntimeHostSha256=text "runtimeHostSha256" v;MechanismAdapterSha256=text "mechanismAdapterSha256" v}
        | "effect-requested" -> keys ["schema";"kind";"action"] v; Observation.EffectRequested(action v["action"])
        | "effect-acknowledged" -> keys ["schema";"kind";"action"] v; Observation.EffectAcknowledged(action v["action"])
        | "effect-response-lost" -> keys ["schema";"kind";"action"] v; Observation.EffectResponseLost(action v["action"])
        | "runs-observed" ->
            keys ["schema";"kind";"repository";"workflow";"qualificationRef";"placementSha";"priorRunIds";"candidateRunIds";"listingComplete"] v
            Observation.RunsObserved {Repository=text "repository" v;Workflow=text "workflow" v;QualificationRef=text "qualificationRef" v;PlacementSha=text "placementSha" v;PriorRunIds=strings "priorRunIds" v 32;CandidateRunIds=strings "candidateRunIds" v 32;ListingComplete=boolean "listingComplete" v}
        | "owned-run-observed" ->
            keys ["schema";"kind";"repository";"workflow";"qualificationRef";"placementSha";"nonce";"runId";"rootOwnershipObserved"] v
            let evidence={Repository=text "repository" v;Workflow=text "workflow" v;QualificationRef=text "qualificationRef" v;PlacementSha=text "placementSha" v;Nonce=text "nonce" v;RunId=text "runId" v;RootOwnershipObserved=boolean "rootOwnershipObserved" v}
            Observation.OwnedRunObserved evidence
        | "owned-run-retired" ->
            keys ["schema";"kind";"repository";"workflow";"qualificationRef";"placementSha";"nonce";"runId";"rootOwnershipObserved";"httpStatus";"observationComplete";"runActive"] v
            Observation.OwnedRunRetired {Repository=text "repository" v;Workflow=text "workflow" v;QualificationRef=text "qualificationRef" v;PlacementSha=text "placementSha" v;Nonce=text "nonce" v;RunId=text "runId" v;RootOwnershipObserved=boolean "rootOwnershipObserved" v;HttpStatus=integer "httpStatus" v;ObservationComplete=boolean "observationComplete" v;RunActive=boolean "runActive" v}
        | "native-evidence-observed" ->
            keys ["schema";"kind";"runId";"repository";"workflow";"qualificationRef";"placementSha";"nonce";"profileSha256";"operationId";"bindingSha256";"evidenceComplete";"nonceJoined";"profileVerified";"activityComplete";"completionCommunicated";"childAck";"parentAck";"writerJoinsHealthy"] v
            Observation.NativeEvidenceObserved { RunId=text "runId" v;Repository=text "repository" v;Workflow=text "workflow" v;QualificationRef=text "qualificationRef" v;PlacementSha=text "placementSha" v;Nonce=text "nonce" v;ProfileSha256=text "profileSha256" v;OperationId=text "operationId" v;BindingSha256=text "bindingSha256" v;EvidenceComplete=boolean "evidenceComplete" v; NonceJoined=boolean "nonceJoined" v; ProfileVerified=boolean "profileVerified" v; ActivityComplete=boolean "activityComplete" v; CompletionCommunicated=boolean "completionCommunicated" v; ChildAck=boolean "childAck" v; ParentAck=boolean "parentAck" v; WriterJoinsHealthy=boolean "writerJoinsHealthy" v }
        | "secret-absence-observed" ->
            keys ["schema";"kind";"role";"repository";"environment";"httpStatus";"listingComplete";"secretPresent"] v
            Observation.SecretAbsenceObserved {Role=ClosedNames.secretOf(text "role" v);Repository=text "repository" v;Environment=text "environment" v;HttpStatus=integer "httpStatus" v;ListingComplete=boolean "listingComplete" v;SecretPresent=boolean "secretPresent" v}
        | "cleanup-call-failed" -> keys ["schema";"kind"] v; Observation.CleanupCallFailed
        | "operation-failed" -> keys ["schema";"kind";"failureClass"] v; require(text "failureClass" v="mechanism-failure") "failure-class-refused"; Observation.OperationFailed OperationFailure.MechanismFailure
        | "time-advanced" -> keys ["schema";"kind";"seconds"] v; Observation.TimeAdvanced(integer "seconds" v)
        | "source-invalidated" -> keys ["schema";"kind"] v; Observation.SourceInvalidated
        | _ -> refuse "observation-kind-refused"

    let private writeAction (writer: Utf8JsonWriter) action =
        let name,argument=ClosedNames.action action
        writer.WriteStartObject();writer.WriteString("name",name)
        match argument with Some value->writer.WriteString("argument",value)|None->writer.WriteNull("argument")
        writer.WriteEndObject()

    let stateBytes state =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteString("schema", state.Schema)
        writer.WriteString("phase", ClosedNames.phase state.Phase)
        let p=state.Prepared
        writer.WriteNumber("sourceGeneration",p.SourceGeneration);writer.WriteString("placementSha",p.PlacementSha);writer.WriteString("placementTree",p.PlacementTree);writer.WriteString("workflowSha256",p.WorkflowSha256);writer.WriteString("recipeSourceSha",p.RecipeSourceSha);writer.WriteString("recipeSourceTree",p.RecipeSourceTree);writer.WriteString("profileSha256",p.ProfileSha256);writer.WriteString("operationId",p.OperationId);writer.WriteString("bindingSha256",p.BindingSha256);writer.WriteString("bindingProducerSha256",p.BindingProducerSha256);writer.WriteString("sourcePinsSha256",p.SourcePinsSha256);writer.WriteString("producerSha256",p.ProducerSha256);writer.WriteString("runtimeHostSha256",p.RuntimeHostSha256);writer.WriteString("mechanismAdapterSha256",p.MechanismAdapterSha256);writer.WriteString("nonce",p.Nonce);writer.WriteString("destinationId",p.DestinationId)
        writer.WriteNumber("currentSourceGeneration",state.CurrentSourceGeneration);writer.WriteString("currentBindingSha256",state.CurrentBindingSha256);writer.WriteNumber("elapsedSeconds",state.ElapsedSeconds);writer.WriteNumber("budgetSeconds",state.BudgetSeconds);writer.WriteBoolean("dispatchIntended",state.DispatchIntended);writer.WriteBoolean("dispatchMayHaveEffect",state.DispatchMayHaveEffect);writer.WriteBoolean("dispatchAcknowledged",state.DispatchAcknowledged);writer.WriteBoolean("effectCheckFresh",state.EffectCheckFresh)
        let writeSecrets (name: string) values =
            writer.WriteStartArray(name)
            values |> Seq.sortBy ClosedNames.secret |> Seq.iter(fun role -> writer.WriteStringValue(ClosedNames.secret role))
            writer.WriteEndArray()
        writeSecrets "secretIntentions" state.SecretIntentions
        writeSecrets "secretAcknowledgments" state.SecretAcknowledgments
        writeSecrets "secretsMayHaveEffect" state.SecretsMayHaveEffect
        writeSecrets "secretAbsenceObserved" state.SecretAbsenceObserved
        writer.WriteStartArray("candidateRuns")
        state.CandidateRuns |> List.iter writer.WriteStringValue
        writer.WriteEndArray()
        match state.OwnedRunId with Some value->writer.WriteString("ownedRunId",value)|None->writer.WriteNull("ownedRunId")
        writer.WriteBoolean("cancellationMayHaveEffect",state.CancellationMayHaveEffect);writer.WriteBoolean("runRetirementObserved",state.RunRetirementObserved);writer.WriteString("nativeDisposition",ClosedNames.native state.NativeDisposition);writer.WriteString("cleanupDisposition",ClosedNames.cleanup state.CleanupDisposition)
        match state.Refusal with Some value->writer.WriteString("refusal",value)|None->writer.WriteNull("refusal")
        writer.WriteEndObject()
        writer.Flush()
        Array.append (stream.ToArray()) [|byte '\n'|]
    let actionsBytes actions =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
        writer.WriteStartObject()
        writer.WriteString("schema", "fsgg.telemetry.host-attempt-actions/1")
        writer.WriteStartArray("actions")
        actions |> List.iter(writeAction writer)
        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()
        Array.append (stream.ToArray()) [|byte '\n'|]

let writeNew path (bytes: byte array) =
    let full = Path.GetFullPath path
    let parent = Path.GetDirectoryName full
    require (Directory.Exists parent) "output-parent-refused"
    let temporary=full+".tmp-"+Guid.NewGuid().ToString("N")
    use stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)
    stream.Write(bytes, 0, bytes.Length)
    stream.Flush true
    stream.Close()
    File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
    try File.Move(temporary, full, false)
    with _ ->
        try File.Delete temporary with _ -> ()
        refuse "output-exists-refused"

let producerSha () = use stream=File.OpenRead(Assembly.GetExecutingAssembly().Location) in Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()
let inheritedChannel name =
    let raw=Environment.GetEnvironmentVariable name
    let mutable descriptor=0
    require(Int32.TryParse(raw,&descriptor)&&descriptor>=3) "root-channel-missing"
    new FileStream(new SafeFileHandle(nativeint descriptor,false),FileAccess.Read,4096,false) :> Stream

[<EntryPoint>]
let main argv =
    AttemptDiagnostics.beginInvocation None None None
    try
        require (argv.Length>0) "command-refused"
        let values=args argv[1..]
        match argv[0] with
        | "prepare" ->
            exactArgs ["request";"output"] values
            let request=Wire.preparation(Wire.read(required "request" values)(64L*1024L))
            let selected=producerSha()
            AttemptDiagnostics.setIdentity (Some request.RecipeSourceSha) (Some selected) None
            let state=AttemptPreparation.prepare selected request
            writeNew(required "output" values)(Wire.stateBytes state);0
        | "next" ->
            exactArgs ["state";"observation";"state-output";"actions-output"] values
            let state=Wire.state(Wire.read(required "state" values)(128L*1024L))
            let observation=Wire.observation(Wire.read(required "observation" values)(64L*1024L))
            let reduction=AttemptReducer.apply observation state
            writeNew(required "state-output" values)(Wire.stateBytes reduction.State)
            writeNew(required "actions-output" values)(Wire.actionsBytes reduction.Actions);0
        | "run" ->
            exactArgs ["request";"output"] values
            let request=Wire.runRequest(Wire.read(required "request" values)(128L*1024L))
            let selected=producerSha()
            AttemptDiagnostics.setIdentity (Some request.Preparation.RecipeSourceSha) (Some selected) None
            require(selected=request.ProducerSha256) "selected-producer-refused"
            let preparationClock=Diagnostics.Stopwatch.StartNew()
            let prepared=AttemptPreparation.prepareWithin selected request.Preparation (request.Preparation.BudgetSeconds*1000)
            let preparationSeconds=int((preparationClock.ElapsedMilliseconds+999L)/1000L)
            let state={prepared with ElapsedSeconds=min prepared.BudgetSeconds preparationSeconds}
            let leaseRoot=Environment.GetEnvironmentVariable "HOST_ATTEMPT_LEASE_ROOT"|>Option.ofObj|>Option.defaultWith(fun()->refuse "lease-root-missing")|>Path.GetFullPath
            let info=DirectoryInfo leaseRoot
            require(info.Exists && not(info.Attributes.HasFlag(FileAttributes.ReparsePoint))) "lease-root-refused"
            let unsafeModes=UnixFileMode.GroupRead|||UnixFileMode.GroupWrite|||UnixFileMode.GroupExecute|||UnixFileMode.OtherRead|||UnixFileMode.OtherWrite|||UnixFileMode.OtherExecute
            require((File.GetUnixFileMode leaseRoot &&& unsafeModes)=enum<UnixFileMode> 0) "lease-root-mode-refused"
            use auth=inheritedChannel "HOST_ATTEMPT_AUTH_FD"
            use ownership=inheritedChannel "HOST_ATTEMPT_OWNERSHIP_FD"
            use verifier=inheritedChannel "HOST_ATTEMPT_VERIFIER_FD"
            use interrupted=new CancellationTokenSource()
            use signal=PosixSignalRegistration.Create(PosixSignal.SIGTERM,fun context->context.Cancel<-true;interrupted.Cancel())
            let readback=Path.Combine(leaseRoot,request.LeaseId+".readback.json")
            let mechanism=AcquiredAttemptMechanism(request,state,{Auth=auth;Ownership=ownership;Verifier=verifier},leaseRoot,readback,interrupted.Token)
            let context={HostBindingDll=request.Preparation.HostBindingDll;RecipeRoot=request.Preparation.RecipeRoot;ProfilePath=request.Preparation.ProfilePath;SourcePinsPath=request.Preparation.SourcePinsPath}
            let steps=ResizeArray<string*string*string>()
            let observe observation reduction =
                match AttemptReducer.canonicalAction observation with
                | Some action -> steps.Add(action,Encoding.UTF8.GetString(Wire.stateBytes reduction.State).Trim(),Encoding.UTF8.GetString(Wire.actionsBytes reduction.Actions).Trim())
                | None -> ()
            let final=AttemptOperation.runObserved observe context state mechanism
            writeNew(required "output" values)(Wire.stateBytes final)
            let stepJson=steps|>Seq.map(fun (action,stateJson,actionsJson) -> $"{{\"action\":{JsonSerializer.Serialize action},\"state\":{stateJson},\"emitted\":{actionsJson}}}")|>String.concat ","
            let trace=Encoding.UTF8.GetBytes($"{{\"schema\":\"fsgg.telemetry.host-attempt-acquired-trace/1\",\"producerSha256\":{JsonSerializer.Serialize selected},\"finalState\":{Encoding.UTF8.GetString(Wire.stateBytes final).Trim()},\"steps\":[{stepJson}]}}\n")
            writeNew(Path.Combine(leaseRoot,request.LeaseId+".trace.json"))trace
            if final.Refusal.IsSome then AttemptDiagnostics.emitToStderr()
            0
        | _ -> refuse "command-refused"
    with
    | AttemptRefusal reason -> AttemptDiagnostics.emitToStderr();Console.Error.WriteLine("host-attempt-refused:"+reason);2
    | :? JsonException -> AttemptDiagnostics.emitToStderr();Console.Error.WriteLine("host-attempt-refused:json-refused");2
    | :? IOException -> AttemptDiagnostics.emitToStderr();Console.Error.WriteLine("host-attempt-refused:io-refused");2
    | error -> AttemptDiagnostics.emitToStderr();Console.Error.WriteLine("host-attempt-refused:unexpected-"+error.GetType().Name);2
