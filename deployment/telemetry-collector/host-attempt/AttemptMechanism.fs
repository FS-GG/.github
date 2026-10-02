namespace Fs.Gg.Telemetry.HostAttempt

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading

module internal ConcreteWire =
    let refuse value=raise(AttemptRefusal value)
    let require condition value=if not condition then refuse value
    let writeNew path (bytes:byte array) =
        let full=Path.GetFullPath path
        let parent=Path.GetDirectoryName full
        require(Directory.Exists parent && not(File.Exists full)) "mechanism-output-refused"
        let temp=full+".tmp-"+Guid.NewGuid().ToString("N")
        File.WriteAllBytes(temp,bytes)
        File.SetUnixFileMode(temp,UnixFileMode.UserRead|||UnixFileMode.UserWrite)
        use stream=new FileStream(temp,FileMode.Open,FileAccess.ReadWrite,FileShare.None)
        stream.Flush true
        File.Move(temp,full,false)
    let jsonString value=JsonSerializer.Serialize(value)
    let actionBytes action =
        let name,arg=ClosedNames.action action
        let argument=match arg with Some v->jsonString v|None->"null"
        Encoding.UTF8.GetBytes($"{{\"schema\":\"fsgg.telemetry.host-attempt-actions/1\",\"actions\":[{{\"name\":{jsonString name},\"argument\":{argument}}}]}}\n")
    let contextBytes (request:PreparationRequest) (state:AttemptState) remaining discoveryStart =
        let owned=match state.OwnedRunId with Some value->jsonString value|None->"null"
        let artifact=Path.Combine(Path.GetTempPath(),"host-attempt-artifacts-"+state.Prepared.DestinationId)
        Directory.CreateDirectory artifact|>ignore
        let start=match discoveryStart with Some value->jsonString value|None->"null"
        let pairs=["placementSha",jsonString state.Prepared.PlacementSha;"nonce",jsonString state.Prepared.Nonce;"hostBindingDll",jsonString request.HostBindingDll;"recipeRoot",jsonString request.RecipeRoot;"recipeSha",jsonString request.RecipeSourceSha;"profilePath",jsonString request.ProfilePath;"sourcePinsPath",jsonString request.SourcePinsPath;"bindingSha256",jsonString state.Prepared.BindingSha256;"ownedRunId",owned;"artifactOutput",jsonString artifact;"discoveryStart",start;"remainingSeconds",string remaining]
        Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.telemetry.host-attempt-transport-context/2\","+(pairs|>List.map(fun(k,v)->jsonString k+":"+v)|>String.concat ",")+"}\n")
    let result bytes expected =
        let actionName,_=ClosedNames.action expected
        let action=AttemptDiagnostics.action expected
        try
            use doc=AttemptAcquisition.document bytes
            let root=AttemptAcquisition.fields doc.RootElement
            AttemptAcquisition.keys ["schema";"results"] root
            require(AttemptAcquisition.text "schema" root="fsgg.telemetry.host-attempt-transport-results/1" && root["results"].ValueKind=JsonValueKind.Array && root["results"].GetArrayLength()=1) "transport-result-refused"
            let v=AttemptAcquisition.fields(root["results"][0])
            AttemptAcquisition.keys ["name";"argument";"exitCode";"stdout";"stderr";"outcome"] v
            let _,expectedArgument=ClosedNames.action expected
            let actualArgument=if v["argument"].ValueKind=JsonValueKind.Null then None else Some(AttemptAcquisition.text "argument" v)
            require(AttemptAcquisition.text "name" v=actionName&&actualArgument=expectedArgument) "transport-action-mismatch"
            let outcome=AttemptAcquisition.text "outcome" v
            if outcome="unknown" then
                AttemptDiagnostics.record Transport (Some action) ResponseLost None "missing" NoException
                MechanismOutcome.ResponseLost,None
            else
                require(outcome="returned" && v["exitCode"].ValueKind=JsonValueKind.Number) "transport-outcome-refused"
                let exitCode=v["exitCode"].GetInt32()
                if exitCode<>0 then AttemptDiagnostics.record Transport (Some action) Returned (Some exitCode) (AttemptDiagnostics.receiverRefusal(AttemptAcquisition.text "stderr" v)) NoException
                MechanismOutcome.Returned exitCode,Some(AttemptAcquisition.text "stdout" v)
        with error ->
            AttemptDiagnostics.record Transport (Some action) Malformed None "missing" (AttemptDiagnostics.exceptionClass error)
            reraise()

/// Concrete fixed mechanism. Root supplies only three inherited, invocation-bound framed channels.
type AcquiredAttemptMechanism(request:ConcreteRunRequest,prepared:AttemptState,channels:RootChannels,leaseRoot:string,readbackPath:string,interrupted:CancellationToken) =
    let stopwatch=Stopwatch.StartNew()
    let mutable sequence=0
    let mutable baseline:(Set<string>*DateTimeOffset) option=None
    let mutable ownedAttempt:(string*int) option=None
    let mutable discoveryStart:string option=None
    let mutable cache=Map.empty<FixedAction,string>
    let mutable authObserved=false
    let mutable interruptionConsumed=false
    let loadedPath=Assembly.GetExecutingAssembly().Location
    let loadedDependencySnapshot=
        AppDomain.CurrentDomain.GetAssemblies()
        |>Array.choose(fun assembly->let path=assembly.Location in if String.IsNullOrEmpty path then None else Some(Path.GetFullPath path,AttemptAcquisition.shaFile path))
    let nextSequence()=sequence<-sequence+1;sequence
    let take action =
        let value=cache.TryFind action|>Option.defaultWith(fun()->raise(AttemptRefusal "acquired-response-missing"))
        cache<-cache.Remove action
        value
    let require condition value=if not condition then raise(AttemptRefusal value)
    let deadline seconds=stopwatch.ElapsedMilliseconds+int64 seconds*1000L
    let remaining limit=let value=int((limit-stopwatch.ElapsedMilliseconds)/1000L) in require(value>0) "aggregate-deadline";value
    let ensureClosure() =
        require(AttemptAcquisition.shaFile loadedPath=request.ProducerSha256 && request.ProducerSha256=prepared.Prepared.ProducerSha256) "loaded-producer-drift"
        require(AttemptAcquisition.shaFile request.Preparation.MechanismAdapterPath=request.TransportSha256 && request.TransportSha256=prepared.Prepared.MechanismAdapterSha256) "transport-drift"
        for path,digest in loadedDependencySnapshot do require(AttemptAcquisition.shaFile path=digest) "loaded-dependency-drift"
    let executeRaw (state:AttemptState) (action:FixedAction) limit (sensitive:string option) =
        ensureClosure()
        let token=Guid.NewGuid().ToString("N")
        let actions=Path.Combine(leaseRoot,$"{request.LeaseId}-{token}.actions.json")
        let context=Path.Combine(leaseRoot,$"{request.LeaseId}-{token}.context.json")
        let output=Path.Combine(leaseRoot,$"{request.LeaseId}-{token}.result.json")
        let lease=Path.Combine(leaseRoot,$"{request.LeaseId}-{nextSequence()}")
        ConcreteWire.writeNew actions (ConcreteWire.actionBytes action)
        let contextState=match action with FixedAction.GetRun run|FixedAction.CancelOwnedRun run|FixedAction.DownloadRunArtifacts run->{state with OwnedRunId=Some run}|_->state
        ConcreteWire.writeNew context (ConcreteWire.contextBytes request.Preparation contextState (remaining limit) discoveryStart)
        let args=[request.Preparation.MechanismAdapterPath;"--actions";actions;"--context";context;"--lease";lease;"--output";output] @ (if sensitive.IsSome then ["--sensitive-stdin"] else [])
        let code,_,error=AttemptPreparation.runBoundedInput "/usr/bin/python3" request.Preparation.RecipeRoot args sensitive ((remaining limit)*1000) 65536
        let diagnosticAction=AttemptDiagnostics.action action
        if code<>0 || not(File.Exists output) then
            AttemptDiagnostics.record TransportAdapter (Some diagnosticAction) ResponseLost (Some code) (AttemptDiagnostics.receiverRefusal error) NoException
            MechanismOutcome.ResponseLost,None
        else ConcreteWire.result (AttemptAcquisition.regularFile output 1048576L) action
    let acquireRun state run remainingSeconds =
        let outcome,raw=executeRaw state (FixedAction.GetRun run) (deadline remainingSeconds) None
        match outcome,raw with MechanismOutcome.Returned 0,Some value->AttemptAcquisition.run(AttemptAcquisition.http value)|_->raise(AttemptRefusal "run-acquisition-refused")
    let validateRun state run attempt acquired =
        AttemptAcquisition.exactRun run state.Prepared.PlacementSha "workflow_dispatch" attempt acquired
    let rootFrame stream remaining=AttemptAcquisition.readFrameWithCancellation stream remaining interrupted
    let effectCheck remainingSeconds =
        ensureClosure()
        AttemptPreparation.revalidateWithin prepared.Prepared request.Preparation (remainingSeconds*1000)
        {SourceGeneration=prepared.Prepared.SourceGeneration;BindingSha256=prepared.Prepared.BindingSha256;ProfileSha256=prepared.Prepared.ProfileSha256;WorkflowSha256=prepared.Prepared.WorkflowSha256;BindingProducerSha256=prepared.Prepared.BindingProducerSha256;RuntimeHostSha256=prepared.Prepared.RuntimeHostSha256;MechanismAdapterSha256=prepared.Prepared.MechanismAdapterSha256}
    do AttemptDiagnostics.setIdentity (Some prepared.Prepared.RecipeSourceSha) (Some prepared.Prepared.ProducerSha256) (Some prepared.Prepared.BindingSha256)
    interface IAttemptMechanism with
        member _.MonotonicMilliseconds()=stopwatch.ElapsedMilliseconds
        member _.ConsumeInterruption()=
            if interrupted.IsCancellationRequested&&not interruptionConsumed then interruptionConsumed<-true;true else false
        member _.Sleep seconds=Threading.Thread.Sleep(seconds*1000)
        member _.AcquireAdmission(context,state,window)=
            try AttemptPreparation.deriveAdmission context.HostBindingDll context.RecipeRoot context.ProfilePath context.SourcePinsPath state.Prepared.Nonce (window.RemainingSeconds*1000)
            with error -> AttemptDiagnostics.record Admission None DiagnosticOutcome.Exception None "missing" (AttemptDiagnostics.exceptionClass error);reraise()
        member _.AcquireEffectCheck(_,window) =
            try effectCheck window.RemainingSeconds
            with error -> AttemptDiagnostics.record EffectCheck None DiagnosticOutcome.Exception None "missing" (AttemptDiagnostics.exceptionClass error);reraise()
        member _.AcquireRunBaseline(state,window) =
            discoveryStart<-Some(DateTimeOffset.UtcNow.AddSeconds(-float AttemptOperation.DiscoverySeconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",Globalization.CultureInfo.InvariantCulture))
            let outcome,raw=executeRaw state FixedAction.ListRuns (deadline window.RemainingSeconds) None
            match outcome,raw with
            | MechanismOutcome.Returned 0,Some value ->
                let envelope=AttemptAcquisition.http value
                let records=AttemptAcquisition.runs envelope
                baseline<-Some(records|>List.map _.Id|>Set.ofList,AttemptAcquisition.responseDate envelope);true
            | _->false
        member _.Execute(action,timeout,sensitive)=
            let diagnosticAction=AttemptDiagnostics.action action
            try
                let limit=deadline timeout
                if action=FixedAction.InspectAuthMetadata && not authObserved then
                    let receipt=AttemptAcquisition.auth request.InvocationId prepared.Prepared.DestinationId (sequence+1) (rootFrame channels.Auth (remaining limit))
                    require(receipt.Repository=AttemptPreparation.Repository && receipt.Environment=AttemptPreparation.Environment && receipt.RequestedRole="native-auth") "auth-metadata-mismatch"
                    sequence<-receipt.Sequence;authObserved<-true
                if action=FixedAction.TransferSecret SecretRole.NativeAuth then require authObserved "auth-metadata-missing"
                let outcome,raw=executeRaw prepared action limit sensitive
                match outcome,raw,action with
                | MechanismOutcome.Returned 0,Some value,FixedAction.ReadPublicIdentity -> AttemptAcquisition.repository AttemptPreparation.Repository (AttemptAcquisition.http value)
                | MechanismOutcome.Returned 0,Some value,FixedAction.InspectAuthMetadata -> AttemptAcquisition.secrets(AttemptAcquisition.http value)|>ignore
                | MechanismOutcome.Returned 0,Some value,FixedAction.InvokeBinding -> AttemptAcquisition.binding prepared.Prepared.BindingSha256 (Encoding.UTF8.GetBytes value)
                | MechanismOutcome.Returned 0,Some value,(FixedAction.ListRuns|FixedAction.GetRun _|FixedAction.ReadSecretAbsence _) -> cache<-cache.Add(action,value)
                | _ -> ()
                outcome
            with error ->
                AttemptDiagnostics.record ActionValidation (Some diagnosticAction) DiagnosticOutcome.Exception None "missing" (AttemptDiagnostics.exceptionClass error)
                reraise()
        member _.AcquireRunListing(state,_)=
            let records=take FixedAction.ListRuns|>AttemptAcquisition.http|>AttemptAcquisition.runs
            let prior,boundary=baseline|>Option.defaultWith(fun()->raise(AttemptRefusal "run-baseline-missing"))
            let eligible=records|>List.filter(fun r->r.HeadSha=state.Prepared.PlacementSha&&r.Event="workflow_dispatch"&&r.CreatedAt>=boundary)
            let candidates=eligible|>List.filter(fun r->not(prior.Contains r.Id))|>List.map _.Id
            {Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;PriorRunIds=Set.toList prior;CandidateRunIds=candidates;ListingComplete=true}
        member _.AcquireOwnedRun(state,run,window)=
            let limit=deadline window.RemainingSeconds
            let acquired=acquireRun state run (remaining limit)
            let receipt=AttemptAcquisition.ownership request.InvocationId state.Prepared.DestinationId (sequence+1) (rootFrame channels.Ownership (remaining limit))
            sequence<-receipt.Sequence
            let matches=acquired.Id=run&&acquired.HeadSha=state.Prepared.PlacementSha&&acquired.Event="workflow_dispatch"&&receipt.RunId=run&&receipt.RunAttempt=acquired.RunAttempt&&receipt.Repository=AttemptPreparation.Repository&&receipt.Workflow=AttemptPreparation.Workflow&&receipt.QualificationRef=AttemptPreparation.QualificationRef&&receipt.PlacementSha=state.Prepared.PlacementSha&&receipt.Nonce=state.Prepared.Nonce&&receipt.Event="workflow_dispatch"
            require matches "owned-run-join-refused"
            ownedAttempt<-Some(run,receipt.RunAttempt)
            {Repository=receipt.Repository;Workflow=receipt.Workflow;QualificationRef=receipt.QualificationRef;PlacementSha=receipt.PlacementSha;Nonce=receipt.Nonce;RunId=run;RootOwnershipObserved=true}
        member _.AcquireNativeEvidence(state,run,window)=
            let limit=deadline window.RemainingSeconds
            let attempt=ownedAttempt|>Option.bind(fun(id,value)->if id=run then Some value else None)|>Option.defaultWith(fun()->raise(AttemptRefusal "owned-run-attempt-missing"))
            let acquired=acquireRun state run (remaining limit)|>validateRun state run attempt
            if acquired.Status<>"completed" then None else
            let receipt=AttemptAcquisition.verifier request.InvocationId state.Prepared.DestinationId (sequence+1) (rootFrame channels.Verifier (remaining limit))
            sequence<-receipt.Sequence
            let expectedArtifact=$"private-native-qualification-{run}-{acquired.RunAttempt}"
            require(receipt.RunId=run && receipt.RunAttempt=acquired.RunAttempt && receipt.Nonce=state.Prepared.Nonce && receipt.ProfileSha256=state.Prepared.ProfileSha256 && receipt.BindingSha256=state.Prepared.BindingSha256 && receipt.SourceSha=state.Prepared.PlacementSha && receipt.ArtifactName=expectedArtifact && receipt.VerifierExitCode=0 && Regex.IsMatch(receipt.VerifierReceiptSha256,"\\A[0-9a-f]{64}\\z")) "verifier-custody-join-refused"
            AttemptAcquisition.canonicalNative state.Prepared.OperationId state.Prepared.Nonce receipt.ResultBytes
            Some {RunId=run;Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;ProfileSha256=receipt.ProfileSha256;OperationId=state.Prepared.OperationId;BindingSha256=receipt.BindingSha256;EvidenceComplete=true;NonceJoined=true;ProfileVerified=true;ActivityComplete=true;CompletionCommunicated=true;ChildAck=true;ParentAck=true;WriterJoinsHealthy=true}
        member _.AcquireSecretAbsence(_,role,_)=
            let action=FixedAction.ReadSecretAbsence role
            let envelope=take action|>AttemptAcquisition.http
            let names=AttemptAcquisition.secrets envelope
            let expected=match role with SecretRole.NativeAuth->"FSGG_V2_HOST_018_NATIVE_AUTH_JSON_B64"|SecretRole.EffectAdmission->"FSGG_V2_HOST_018_EFFECT_ADMISSION"
            {Role=role;Repository=AttemptPreparation.Repository;Environment=AttemptPreparation.Environment;HttpStatus=envelope.Status;ListingComplete=true;SecretPresent=List.contains expected names}
        member _.AcquireRunRetirement(state,run,window)=
            let attempt=ownedAttempt|>Option.bind(fun(id,value)->if id=run then Some value else None)|>Option.defaultWith(fun()->raise(AttemptRefusal "owned-run-attempt-missing"))
            let acquired=take(FixedAction.GetRun run)|>AttemptAcquisition.http|>AttemptAcquisition.run|>validateRun state run attempt
            {Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;RunId=run;RootOwnershipObserved=true;HttpStatus=200;ObservationComplete=true;RunActive=acquired.Status<>"completed"}
        member _.PersistReadback(state,_)=
            let refusal=match state.Refusal with Some value->ConcreteWire.jsonString value|None->"null"
            let bytes=Encoding.UTF8.GetBytes($"{{\"schema\":\"fsgg.telemetry.host-attempt-readback/1\",\"invocationId\":{ConcreteWire.jsonString request.InvocationId},\"destinationId\":{ConcreteWire.jsonString state.Prepared.DestinationId},\"phase\":{ConcreteWire.jsonString(ClosedNames.phase state.Phase)},\"nativeDisposition\":{ConcreteWire.jsonString(ClosedNames.native state.NativeDisposition)},\"cleanupDisposition\":{ConcreteWire.jsonString(ClosedNames.cleanup state.CleanupDisposition)},\"refusal\":{refusal}}}\n")
            ConcreteWire.writeNew readbackPath bytes
