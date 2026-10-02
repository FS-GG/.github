namespace Fs.Gg.Telemetry.HostAttempt.Tests

open System
open System.Buffers.Binary
open System.Diagnostics
open System.IO
open System.IO.Pipes
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Fs.Gg.Telemetry.HostAttempt
open Xunit

module Helpers =
    let digest (value:string) = SHA256.HashData(Encoding.UTF8.GetBytes value) |> Convert.ToHexString |> fun value -> value.ToLowerInvariant()
    let prepared () =
        let identity =
            { SourceGeneration=1;PlacementSha=String.replicate 40 "a";PlacementTree=String.replicate 40 "b";WorkflowSha256=String.replicate 64 "c"
              RecipeSourceSha=AttemptPreparation.RecipeSha;RecipeSourceTree=AttemptPreparation.RecipeTree;ProfileSha256=String.replicate 64 "d";OperationId=AttemptPreparation.OperationId
              BindingSha256=String.replicate 64 "e";BindingProducerSha256=String.replicate 64 "f";SourcePinsSha256=String.replicate 64 "1";ProducerSha256=String.replicate 64 "2";RuntimeHostSha256=String.replicate 64 "3";MechanismAdapterSha256=String.replicate 64 "4";Nonce="attempt-o-0001";DestinationId="destination-o-0001" }
        { Schema="fsgg.telemetry.host-attempt-state/2";Phase=Phase.Prepared;Prepared=identity;CurrentSourceGeneration=1;CurrentBindingSha256=identity.BindingSha256;ElapsedSeconds=0;BudgetSeconds=2700
          DispatchIntended=false;DispatchMayHaveEffect=false;DispatchAcknowledged=false;EffectCheckFresh=false;SecretIntentions=Set.empty;SecretAcknowledgments=Set.empty;SecretsMayHaveEffect=Set.empty;SecretAbsenceObserved=Set.empty
          CandidateRuns=[];OwnedRunId=None;CancellationMayHaveEffect=false;RunRetirementObserved=false;NativeDisposition=NativeDisposition.NativeUnknown;CleanupDisposition=CleanupDisposition.CleanupUnknown;Refusal=None }
    let apply observation state = (AttemptReducer.apply observation state).State
    let effectChecked state=apply (Observation.EffectCheckObserved {SourceGeneration=state.Prepared.SourceGeneration;BindingSha256=state.Prepared.BindingSha256;ProfileSha256=state.Prepared.ProfileSha256;WorkflowSha256=state.Prepared.WorkflowSha256;BindingProducerSha256=state.Prepared.BindingProducerSha256;RuntimeHostSha256=state.Prepared.RuntimeHostSha256;MechanismAdapterSha256=state.Prepared.MechanismAdapterSha256}) state
    let placed () =
        let start=apply Observation.PlacementObserved (prepared()) |> effectChecked
        let auth=apply (Observation.EffectRequested(FixedAction.TransferSecret SecretRole.NativeAuth)) start
        let authAck=apply (Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.NativeAuth)) auth
        let admission=apply (Observation.EffectRequested(FixedAction.TransferSecret SecretRole.EffectAdmission)) (effectChecked authAck)
        apply (Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.EffectAdmission)) admission
    let dispatched () =
        let intended=apply (Observation.EffectRequested FixedAction.DispatchOnce) (placed()|>effectChecked)
        apply (Observation.EffectAcknowledged FixedAction.DispatchOnce) intended
    let listing state candidates={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;PriorRunIds=["prior-run"];CandidateRunIds=candidates;ListingComplete=true}
    let ownership state run={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;RunId=run;RootOwnershipObserved=true}
    let retirement state run={Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;RunId=run;RootOwnershipObserved=true;HttpStatus=200;ObservationComplete=true;RunActive=false}
    let watching () = let state=dispatched() in let candidate=apply(Observation.RunsObserved(listing state ["run-1"]))state in apply(Observation.OwnedRunObserved(ownership candidate "run-1"))candidate
    let evidence state joined={RunId="run-1";Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow;QualificationRef=AttemptPreparation.QualificationRef;PlacementSha=state.Prepared.PlacementSha;Nonce=state.Prepared.Nonce;ProfileSha256=state.Prepared.ProfileSha256;OperationId=state.Prepared.OperationId;BindingSha256=state.Prepared.BindingSha256;EvidenceComplete=true;NonceJoined=joined;ProfileVerified=true;ActivityComplete=true;CompletionCommunicated=true;ChildAck=true;ParentAck=true;WriterJoinsHealthy=true}
    let absence role={Role=role;Repository=AttemptPreparation.Repository;Environment=AttemptPreparation.Environment;HttpStatus=200;ListingComplete=true;SecretPresent=false}
    let run executable working args =
        let start=ProcessStartInfo(executable,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=working)
        for arg in args do start.ArgumentList.Add arg
        use child=Process.Start start
        let output = child.StandardOutput.ReadToEnd()
        let error = child.StandardError.ReadToEnd()
        child.WaitForExit()
        child.ExitCode, output, error

open Helpers

type TempDirectory() =
    let path = Path.Combine(Path.GetTempPath(), "host-attempt-" + Guid.NewGuid().ToString("N"))
    do Directory.CreateDirectory(path) |> ignore
    member _.Path = path
    interface IDisposable with member _.Dispose() = Directory.Delete(path, true)

module AcquisitionTests =
    let response status headers body = $"HTTP/1.1 {status} status\r\n{headers}\r\n\r\n{body}"
    [<Fact>]
    let ``HTTP refusal pagination and duplicate run census stay incomplete`` () =
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.secrets(AttemptAcquisition.http(response 403 "content-type: application/json" "{}"))|>ignore)|>ignore
        let page=response 200 "link: <next>; rel=\"next\"" "{\"total_count\":0,\"workflow_runs\":[]}"
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.runs(AttemptAcquisition.http page)|>ignore)|>ignore
        let wrongRepository=response 200 "content-type: application/json" "{\"full_name\":\"FS-GG/wrong\"}"
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.repository AttemptPreparation.Repository (AttemptAcquisition.http wrongRepository)|>ignore)|>ignore
        let duplicate="{\"total_count\":2,\"workflow_runs\":[{\"id\":1,\"head_sha\":\"a\",\"event\":\"workflow_dispatch\",\"status\":\"queued\",\"conclusion\":null,\"created_at\":\"2026-10-02T00:00:00Z\",\"run_attempt\":1},{\"id\":1,\"head_sha\":\"a\",\"event\":\"workflow_dispatch\",\"status\":\"queued\",\"conclusion\":null,\"created_at\":\"2026-10-02T00:00:01Z\",\"run_attempt\":1}]}"
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.runs(AttemptAcquisition.http(response 200 "content-type: application/json" duplicate))|>ignore)|>ignore
        let rich="{\"id\":77,\"head_sha\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"event\":\"workflow_dispatch\",\"status\":\"completed\",\"conclusion\":\"success\",\"run_attempt\":1,\"head_branch\":\"qualification/v2-host-native-20260930\",\"workflow_id\":123,\"url\":\"https://api.github.invalid/run/77\"}"
        let acquired=AttemptAcquisition.run(AttemptAcquisition.http(response 200 "content-type: application/json" rich))
        AttemptAcquisition.exactRun "77" (String.replicate 40 "a") "workflow_dispatch" 1 acquired|>ignore
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.exactRun "999" (String.replicate 40 "a") "workflow_dispatch" 1 acquired|>ignore)|>ignore
    [<Fact>]
    let ``real inherited pipe read observes its deadline`` () =
        let name="host-attempt-deadline-"+Guid.NewGuid().ToString("N")
        use server=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)
        let connected=server.WaitForConnectionAsync()
        use client=new NamedPipeClientStream(".",name,PipeDirection.Out,PipeOptions.Asynchronous)
        client.Connect(1000);connected.GetAwaiter().GetResult()
        let elapsed=Diagnostics.Stopwatch.StartNew()
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.readFrame server 1|>ignore)|>ignore
        Assert.InRange(elapsed.Elapsed.TotalSeconds,0.5,3.0)
    [<Fact>]
    let ``framed root evidence binds invocation destination sequence and digest`` () =
        let payload=Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.telemetry.host-attempt-auth-metadata/1\",\"invocationId\":\"invocation-1\",\"destinationId\":\"destination-1\",\"sequence\":2,\"repository\":\"FS-GG/FS.GG.GitHub.Substrate.Sandbox\",\"environment\":\"v2-host-01-8-native-private\",\"requestedRole\":\"native-auth\"}")
        let framed=Array.zeroCreate<byte>(payload.Length+4)
        BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(0,4),payload.Length)
        payload.CopyTo(framed,4)
        use stream=new MemoryStream(framed)
        let actual=AttemptAcquisition.readFrame stream 1|>AttemptAcquisition.auth "invocation-1" "destination-1" 2
        Assert.Equal(2,actual.Sequence)
        use wrong=new MemoryStream(framed)
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.readFrame wrong 1|>AttemptAcquisition.auth "invocation-1" "destination-1" 3|>ignore)|>ignore

    [<Fact>]
    let ``binding authority cleanup unknown is terminal without a second call`` () =
        AttemptDiagnostics.beginInvocation (Some AttemptPreparation.RecipeSha) (Some(String.replicate 64 "a")) None
        let mutable attempts=0
        Assert.Throws<AttemptRefusal>(fun()->
            AttemptPreparation.runHostBindingAttempt "binding-test-refused" (fun()->
            attempts<-attempts+1
            2,"","host-binding-refused:process-cleanup-unknown\n")|>ignore)|>ignore
        Assert.Equal(1,attempts)
        let diagnostic=AttemptDiagnostics.snapshot()|>Option.defaultWith(fun()->failwith "diagnostic missing")
        Assert.Equal(PreparationRender,diagnostic.Stage)
        Assert.Equal(Some 2,diagnostic.ExitCode)
        Assert.Equal("process-cleanup-unknown",diagnostic.ReceiverRefusal)
        Assert.Equal("unrecognized",AttemptDiagnostics.receiverRefusal(String.replicate 65536 "x"+"SENTINEL"))

    [<Fact>]
    let ``transport diagnostic retains first closed failure without raw stderr or success material`` () =
        AttemptDiagnostics.beginInvocation (Some AttemptPreparation.RecipeSha) (Some(String.replicate 64 "a")) (Some(String.replicate 64 "b"))
        let secret="SENTINEL-PRIVATE-DO-NOT-RETAIN"
        let failure=$"{{\"schema\":\"fsgg.telemetry.host-attempt-transport-results/1\",\"results\":[{{\"name\":\"read-public-identity\",\"argument\":null,\"exitCode\":2,\"stdout\":\"\",\"stderr\":\"{secret}\",\"outcome\":\"returned\"}}]}}"
        let outcome,_=ConcreteWire.result(Encoding.UTF8.GetBytes failure) FixedAction.ReadPublicIdentity
        Assert.Equal(MechanismOutcome.Returned 2,outcome)
        let successMaterial=String.replicate 64 "c"
        let success=$"{{\"schema\":\"fsgg.telemetry.host-attempt-transport-results/1\",\"results\":[{{\"name\":\"invoke-binding\",\"argument\":null,\"exitCode\":0,\"stdout\":\"{successMaterial}\",\"stderr\":\"\",\"outcome\":\"returned\"}}]}}"
        ConcreteWire.result(Encoding.UTF8.GetBytes success) FixedAction.InvokeBinding|>ignore
        let text=AttemptDiagnostics.snapshot()|>Option.map AttemptDiagnostics.text|>Option.defaultWith(fun()->failwith "diagnostic missing")
        Assert.Contains("\"action\":\"read-public-identity\"",text)
        Assert.Contains("\"receiverRefusal\":\"unrecognized\"",text)
        Assert.DoesNotContain(secret,text)
        Assert.DoesNotContain(successMaterial,text)

    [<Fact>]
    let ``transport response loss and malformed result retain closed categories`` () =
        AttemptDiagnostics.beginInvocation None None None
        let lost="{\"schema\":\"fsgg.telemetry.host-attempt-transport-results/1\",\"results\":[{\"name\":\"inspect-auth-metadata\",\"argument\":null,\"exitCode\":null,\"stdout\":\"\",\"stderr\":\"\",\"outcome\":\"unknown\"}]}"
        let outcome,_=ConcreteWire.result(Encoding.UTF8.GetBytes lost) FixedAction.InspectAuthMetadata
        Assert.Equal(MechanismOutcome.ResponseLost,outcome)
        let lostDiagnostic=AttemptDiagnostics.snapshot()|>Option.defaultWith(fun()->failwith "diagnostic missing")
        Assert.Equal(ResponseLost,lostDiagnostic.Outcome)
        Assert.Equal(Some InspectAuthMetadata,lostDiagnostic.Action)
        AttemptDiagnostics.beginInvocation None None None
        Assert.Throws<AttemptRefusal>(fun()->ConcreteWire.result(Encoding.UTF8.GetBytes "{}") FixedAction.InvokeBinding|>ignore)|>ignore
        let malformed=AttemptDiagnostics.snapshot()|>Option.defaultWith(fun()->failwith "diagnostic missing")
        Assert.Equal(Malformed,malformed.Outcome)
        Assert.Equal(Some InvokeBinding,malformed.Action)

module ReducerTests =
    [<Fact>]
    let ``old source or profile cannot reach a secret action`` () =
        let state=prepared()
        let result=AttemptReducer.apply (Observation.SourceRevalidated(2,state.Prepared.BindingSha256,String.replicate 64 "5",state.Prepared.WorkflowSha256)) state
        Assert.Equal(Phase.Refused,result.State.Phase);Assert.Empty(result.Actions);Assert.Empty(result.State.SecretsMayHaveEffect)

    [<Fact>]
    let ``partial secret response loss retains cleanup obligation`` () =
        let start=apply Observation.PlacementObserved (prepared()) |> effectChecked
        let intended=apply (Observation.EffectRequested(FixedAction.TransferSecret SecretRole.NativeAuth)) start
        let result=AttemptReducer.apply (Observation.EffectResponseLost(FixedAction.TransferSecret SecretRole.NativeAuth)) intended
        Assert.Equal(Phase.CleanupPending,result.State.Phase);Assert.Contains(SecretRole.NativeAuth,result.State.SecretsMayHaveEffect)
        Assert.Equal(CleanupDisposition.CleanupPending,result.State.CleanupDisposition)

    [<Fact>]
    let ``lost dispatch response cannot create a retry`` () =
        let intended=apply (Observation.EffectRequested FixedAction.DispatchOnce) (placed()|>effectChecked)
        let lost=AttemptReducer.apply (Observation.EffectResponseLost FixedAction.DispatchOnce) intended
        Assert.True(lost.State.DispatchIntended);Assert.True(lost.State.DispatchMayHaveEffect);Assert.False(lost.State.DispatchAcknowledged)
        let repeated=AttemptReducer.apply (Observation.EffectRequested FixedAction.DispatchOnce) lost.State
        Assert.True(repeated.State.Refusal.IsSome);Assert.DoesNotContain(FixedAction.DispatchOnce,repeated.Actions)

    [<Fact>]
    let ``duplicate secret acknowledgment cannot authorize dispatch`` () =
        let start=apply Observation.PlacementObserved (prepared()) |> effectChecked
        let auth=apply (Observation.EffectRequested(FixedAction.TransferSecret SecretRole.NativeAuth)) start
        let once=apply (Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.NativeAuth)) auth
        let duplicate=apply (Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.NativeAuth)) once
        let late=apply (Observation.EffectAcknowledged(FixedAction.TransferSecret SecretRole.EffectAdmission)) duplicate
        let dispatch=AttemptReducer.apply (Observation.EffectRequested FixedAction.DispatchOnce) late
        Assert.True(dispatch.State.Refusal.IsSome);Assert.False(dispatch.State.DispatchIntended);Assert.DoesNotContain(FixedAction.DispatchOnce,dispatch.Actions)

    [<Fact>]
    let ``admission cannot be requested before authenticated secret acknowledgment`` () =
        let start=apply Observation.PlacementObserved (prepared()) |> effectChecked
        let result=AttemptReducer.apply (Observation.EffectRequested(FixedAction.TransferSecret SecretRole.EffectAdmission)) start
        Assert.True(result.State.Refusal.IsSome);Assert.Empty(result.State.SecretsMayHaveEffect);Assert.Empty(result.Actions)

    [<Fact>]
    let ``ambiguous discovery owns no run and cannot cancel`` () =
        let state=dispatched()
        let ambiguous=apply (Observation.RunsObserved(listing state ["run-1";"run-2"])) state
        Assert.Equal(Phase.CleanupPending,ambiguous.Phase);Assert.True(ambiguous.OwnedRunId.IsNone)
        let cancel=AttemptReducer.apply (Observation.EffectRequested(FixedAction.CancelOwnedRun "run-1")) ambiguous
        Assert.True(cancel.State.Refusal.IsSome);Assert.DoesNotContain(FixedAction.CancelOwnedRun "run-1",cancel.Actions)

    [<Fact>]
    let ``singleton listing remains candidate until root ownership observation`` () =
        let state=dispatched()
        let candidate=apply (Observation.RunsObserved(listing state ["run-1"])) state
        Assert.True(candidate.OwnedRunId.IsNone)
        let cancel=AttemptReducer.apply (Observation.EffectRequested(FixedAction.CancelOwnedRun "run-1")) candidate
        Assert.True(cancel.State.Refusal.IsSome);Assert.DoesNotContain(FixedAction.CancelOwnedRun "run-1",cancel.Actions)

    [<Fact>]
    let ``deletion acknowledgment does not prove absence`` () =
        let start=placed()
        let deleting=apply (Observation.EffectRequested(FixedAction.DeleteSecret SecretRole.NativeAuth)) start
        let acknowledged=apply (Observation.EffectAcknowledged(FixedAction.DeleteSecret SecretRole.NativeAuth)) deleting
        Assert.Equal(CleanupDisposition.CleanupPending,acknowledged.CleanupDisposition);Assert.DoesNotContain(SecretRole.NativeAuth,acknowledged.SecretAbsenceObserved)

    [<Fact>]
    let ``native acceptance requires every authoritative observation`` () =
        let watching=watching()
        let unknown=apply (Observation.NativeEvidenceObserved(evidence watching false)) watching
        let accepted=apply (Observation.NativeEvidenceObserved(evidence watching true)) watching
        Assert.Equal(NativeDisposition.NativeUnknown,unknown.NativeDisposition);Assert.Equal(NativeDisposition.NativeAccepted,accepted.NativeDisposition)

    [<Fact>]
    let ``cancellation and deadline keep later native evidence unknown`` () =
        let watching=watching()
        let cancelling=apply (Observation.EffectRequested(FixedAction.CancelOwnedRun "run-1")) (effectChecked watching)
        let afterCancel=apply (Observation.NativeEvidenceObserved(evidence cancelling true)) cancelling
        let expired=apply (Observation.TimeAdvanced 2700) watching
        let afterDeadline=apply (Observation.NativeEvidenceObserved(evidence expired true)) expired
        Assert.Equal(NativeDisposition.NativeUnknown,afterCancel.NativeDisposition);Assert.Equal(NativeDisposition.NativeUnknown,afterDeadline.NativeDisposition)

    [<Fact>]
    let ``secret absence readback proves only secret cleanup with unresolved dispatch`` () =
        let state=placed() |> effectChecked |> apply (Observation.EffectRequested FixedAction.DispatchOnce) |> apply (Observation.EffectResponseLost FixedAction.DispatchOnce)
        let one=apply (Observation.SecretAbsenceObserved(absence SecretRole.NativeAuth)) state
        let final=apply (Observation.SecretAbsenceObserved(absence SecretRole.EffectAdmission)) one
        Assert.Equal(Phase.CleanupPending,final.Phase);Assert.Equal(CleanupDisposition.SecretsAbsent,final.CleanupDisposition);Assert.Equal(NativeDisposition.NativeUnknown,final.NativeDisposition);Assert.False(final.RunRetirementObserved)

    [<Fact>]
    let ``incomplete or active run readback cannot prove retirement`` () =
        let state=watching() |> apply (Observation.NativeEvidenceObserved(evidence (watching()) true))
        let incomplete=retirement state "run-1" |> fun value->{value with ObservationComplete=false}
        let active=retirement state "run-1" |> fun value->{value with RunActive=true}
        let afterIncomplete=apply (Observation.OwnedRunRetired incomplete) state
        let afterActive=apply (Observation.OwnedRunRetired active) state
        Assert.False(afterIncomplete.RunRetirementObserved);Assert.False(afterActive.RunRetirementObserved)

    [<Fact>]
    let ``aggregate time never replenishes and exhaustion stays unknown`` () =
        let first=apply (Observation.TimeAdvanced 2600) (prepared())
        let final=apply (Observation.TimeAdvanced 100) first
        Assert.Equal(2700,final.ElapsedSeconds);Assert.Equal(Phase.CleanupPending,final.Phase);Assert.Equal(NativeDisposition.NativeUnknown,final.NativeDisposition)

type FakeMechanism(failBinding:bool, ?nativeJoined:bool) =
    let calls=ResizeArray<FixedAction*int*bool>()
    let mutable now=0L
    member _.Calls=calls |> Seq.toList
    interface IAttemptMechanism with
        member _.MonotonicMilliseconds()=now
        member _.ConsumeInterruption()=false
        member _.Sleep seconds=now<-now+int64 seconds*1000L
        member _.AcquireAdmission(_,_,_)=String.replicate 64 "a"
        member _.AcquireEffectCheck(state,_)={SourceGeneration=state.Prepared.SourceGeneration;BindingSha256=state.Prepared.BindingSha256;ProfileSha256=state.Prepared.ProfileSha256;WorkflowSha256=state.Prepared.WorkflowSha256;BindingProducerSha256=state.Prepared.BindingProducerSha256;RuntimeHostSha256=state.Prepared.RuntimeHostSha256;MechanismAdapterSha256=state.Prepared.MechanismAdapterSha256}
        member _.AcquireRunBaseline(_,_) = true
        member _.Execute(action,timeout,sensitive)=calls.Add(action,timeout,sensitive.IsSome);now<-now+1000L;if failBinding && action=FixedAction.InvokeBinding then MechanismOutcome.Returned 2 else MechanismOutcome.Returned 0
        member _.AcquireRunListing(state,_)=listing state ["run-1"]
        member _.AcquireOwnedRun(state,run,_)=ownership state run
        member _.AcquireNativeEvidence(state,_,_)=Some(evidence state (defaultArg nativeJoined true))
        member _.AcquireSecretAbsence(_,role,_)=absence role
        member _.AcquireRunRetirement(state,run,_)=retirement state run
        member _.PersistReadback(_,_) = calls.Add(FixedAction.EmitRootReadback,30,false)

type ControlledMechanism(mode:string) =
    let calls=ResizeArray<FixedAction*int>()
    let windows=ResizeArray<string*OperationWindow>()
    let mutable now=0L
    let mutable retirementReads=0
    let mutable interrupted=false
    let tick amount=now<-now+amount
    member _.Calls=calls|>Seq.toList
    member _.Windows=windows|>Seq.toList
    member _.Now=now
    interface IAttemptMechanism with
        member _.MonotonicMilliseconds()=now
        member _.ConsumeInterruption()=if interrupted then interrupted<-false;true else false
        member _.Sleep seconds=tick(int64 seconds*1000L)
        member _.AcquireAdmission(_,_,window)=windows.Add("admission",window);tick 1000L;String.replicate 64 "a"
        member _.AcquireEffectCheck(state,window)=
            windows.Add("effect",window);tick 1000L
            if mode="exception-after-auth" && state.SecretAcknowledgments.Contains SecretRole.NativeAuth then failwith "controlled-acquisition-failure"
            {SourceGeneration=state.Prepared.SourceGeneration;BindingSha256=state.Prepared.BindingSha256;ProfileSha256=state.Prepared.ProfileSha256;WorkflowSha256=state.Prepared.WorkflowSha256;BindingProducerSha256=state.Prepared.BindingProducerSha256;RuntimeHostSha256=state.Prepared.RuntimeHostSha256;MechanismAdapterSha256=state.Prepared.MechanismAdapterSha256}
        member _.AcquireRunBaseline(_,window)=windows.Add("baseline",window);tick 1000L;true
        member _.Execute(action,timeout,sensitive)=
            calls.Add(action,timeout)
            tick(
                if mode="late-final" && action=FixedAction.EmitRootReadback then 2701000L
                elif mode="discovery-list-deadline" && action=FixedAction.ListRuns then int64 timeout*1000L
                else 1000L)
            MechanismOutcome.Returned 0
        member _.AcquireRunListing(state,window)=
            windows.Add("listing",window)
            if mode="discovery-list-deadline" then listing state []
            else
                tick(if mode="discovery-acquisition-deadline" then int64 window.RemainingSeconds*1000L else 1000L)
                if mode="exception-after-dispatch" then failwith "controlled-listing-failure" else listing state ["run-1"]
        member _.AcquireOwnedRun(state,run,window)=windows.Add("ownership",window);tick 1000L;ownership state run
        member _.AcquireNativeEvidence(state,_,window)=
            windows.Add("native",window);tick 1000L
            if mode="exception-after-native" then failwith "controlled-native-failure"
            elif mode="signal-after-owned" then interrupted<-true;raise(OperationCanceledException("controlled-signal"))
            else Some(evidence state true)
        member _.AcquireSecretAbsence(_,role,window)=windows.Add("absence",window);tick 1000L;absence role
        member _.AcquireRunRetirement(state,run,window)=
            windows.Add("retirement",window);tick 1000L;retirementReads<-retirementReads+1
            let observed=retirement state run
            if mode="exception-after-native" && retirementReads=1 then {observed with RunActive=true} else observed
        member _.PersistReadback(_,window)=windows.Add("readback",window);calls.Add(FixedAction.EmitRootReadback,window.RemainingSeconds);tick(if mode="late-final" then 2701000L else 1000L)

module OperationTests =
    let operationContext () =
        let root=Environment.GetEnvironmentVariable "HOST_ATTEMPT_RECIPE_ROOT"
        {HostBindingDll=Environment.GetEnvironmentVariable "HOST_ATTEMPT_BINDING_DLL";RecipeRoot=root;ProfilePath=Path.Combine(root,"deployment/telemetry-collector/native-operation-v1.json");SourcePinsPath=Environment.GetEnvironmentVariable "HOST_ATTEMPT_SOURCE_PINS"}

    [<Fact>]
    let ``loaded production assembly equals the selected producer pin`` () =
        let selected=Environment.GetEnvironmentVariable "HOST_ATTEMPT_PRODUCER_DLL"
        let loaded=typeof<AttemptState>.Assembly.Location
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes selected)),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes loaded)))
    [<Fact>]
    let ``FSharp process reader refuses while streaming beyond cap`` () =
        Assert.Throws<AttemptRefusal>(fun()->AttemptPreparation.runBounded "/usr/bin/python3" "/tmp" ["-c";"import sys;sys.stdout.write('x'*70000)"] 5000 65536|>ignore)|>ignore

    [<Fact>]
    let ``FSharp process timeout settles boundedly`` () =
        let started=Diagnostics.Stopwatch.StartNew()
        Assert.Throws<AttemptRefusal>(fun()->AttemptPreparation.runBounded "/usr/bin/python3" "/tmp" ["-c";"import time;time.sleep(30)"] 100 1024|>ignore)|>ignore
        Assert.InRange(started.Elapsed.TotalSeconds,0.0,6.0)

    [<Fact>]
    let ``failed binding check stops before every effect`` () =
        let mechanism=FakeMechanism(true)
        let context={HostBindingDll="/unused";RecipeRoot="/unused";ProfilePath="/unused";SourcePinsPath="/unused"}
        let final=AttemptOperation.run context (prepared()) mechanism
        Assert.True(final.Refusal.IsSome)
        Assert.DoesNotContain(mechanism.Calls,fun(action,_,_)->match action with FixedAction.TransferSecret _|FixedAction.DispatchOnce->true|_->false)

    [<Fact>]
    let ``operation uses derive in memory fixed ordering and bounded timeouts`` () =
        let mechanism=FakeMechanism(false)
        let root=Environment.GetEnvironmentVariable "HOST_ATTEMPT_RECIPE_ROOT"
        let context={HostBindingDll=Environment.GetEnvironmentVariable "HOST_ATTEMPT_BINDING_DLL";RecipeRoot=root;ProfilePath=Path.Combine(root,"deployment/telemetry-collector/native-operation-v1.json");SourcePinsPath=Environment.GetEnvironmentVariable "HOST_ATTEMPT_SOURCE_PINS"}
        let final=AttemptOperation.run context (prepared()) mechanism
        let calls=mechanism.Calls
        Assert.Equal(NativeDisposition.NativeAccepted,final.NativeDisposition)
        Assert.Equal(CleanupDisposition.SecretsAbsent,final.CleanupDisposition)
        Assert.True(final.RunRetirementObserved);Assert.Equal(Phase.Finalized,final.Phase)
        Assert.Contains(calls,fun(action,timeout,sensitive)->action=FixedAction.TransferSecret SecretRole.EffectAdmission && timeout<=45 && sensitive)
        Assert.Equal(1,calls|>List.filter(fun(action,_,_)->action=FixedAction.DispatchOnce)|>List.length)
        Assert.Equal(2,calls|>List.filter(fun(action,_,_)->match action with FixedAction.DeleteSecret _ -> true | _ -> false)|>List.length)
        Assert.Equal(2,calls|>List.filter(fun(action,_,_)->match action with FixedAction.ReadSecretAbsence _ -> true | _ -> false)|>List.length)
        Assert.Contains(calls,fun(action,_,_)->action=FixedAction.GetRun "run-1")
        Assert.Contains(calls,fun(action,_,_)->action=FixedAction.EmitRootReadback)
        Assert.All(calls,fun(_,timeout,_)->Assert.InRange(timeout,1,120))

    [<Fact>]
    let ``unknown native evidence cancels only the owned run and completes typed readbacks`` () =
        let mechanism=FakeMechanism(false,false)
        let root=Environment.GetEnvironmentVariable "HOST_ATTEMPT_RECIPE_ROOT"
        let context={HostBindingDll=Environment.GetEnvironmentVariable "HOST_ATTEMPT_BINDING_DLL";RecipeRoot=root;ProfilePath=Path.Combine(root,"deployment/telemetry-collector/native-operation-v1.json");SourcePinsPath=Environment.GetEnvironmentVariable "HOST_ATTEMPT_SOURCE_PINS"}
        let final=AttemptOperation.run context (prepared()) mechanism
        Assert.Equal(NativeDisposition.NativeUnknown,final.NativeDisposition)
        Assert.True(final.CancellationMayHaveEffect);Assert.True(final.RunRetirementObserved)
        Assert.Equal(Phase.Finalized,final.Phase)
        Assert.Equal(1,mechanism.Calls|>List.filter(fun(action,_,_)->action=FixedAction.CancelOwnedRun "run-1")|>List.length)

    [<Fact>]
    let ``absolute aggregate deadline includes resumed elapsed and refuses final late evidence`` () =
        let resumed=(AttemptReducer.apply(Observation.TimeAdvanced 2699)(prepared())).State
        let resumedMechanism=ControlledMechanism("positive")
        let resumedFinal=AttemptOperation.run (operationContext()) resumed resumedMechanism
        Assert.Equal(2700,resumedFinal.ElapsedSeconds);Assert.Equal(NativeDisposition.NativeUnknown,resumedFinal.NativeDisposition)
        Assert.DoesNotContain(resumedMechanism.Calls,fun(action,_)->match action with FixedAction.TransferSecret _ -> true | _ -> false)
        let lateMechanism=ControlledMechanism("late-final")
        let lateFinal=AttemptOperation.run (operationContext()) (prepared()) lateMechanism
        Assert.Equal(2700,lateFinal.ElapsedSeconds);Assert.Equal(NativeDisposition.NativeUnknown,lateFinal.NativeDisposition);Assert.NotEqual(Phase.Finalized,lateFinal.Phase)

    [<Fact>]
    let ``post-auth acquisition exception preserves obligation and drives typed retirement`` () =
        let mechanism=ControlledMechanism("exception-after-auth")
        let final=AttemptOperation.run (operationContext()) (prepared()) mechanism
        Assert.Equal(Some "mechanism-failure",final.Refusal)
        Assert.Contains(SecretRole.NativeAuth,final.SecretsMayHaveEffect);Assert.Contains(SecretRole.NativeAuth,final.SecretAbsenceObserved)
        Assert.Contains(mechanism.Calls,fun(action,_)->action=FixedAction.DeleteSecret SecretRole.NativeAuth)

    [<Fact>]
    let ``post-dispatch acquisition exception preserves dispatch uncertainty and bounded cleanup`` () =
        let mechanism=ControlledMechanism("exception-after-dispatch")
        let final=AttemptOperation.run (operationContext()) (prepared()) mechanism
        Assert.Equal(Some "mechanism-failure",final.Refusal);Assert.True(final.DispatchMayHaveEffect);Assert.True(final.DispatchAcknowledged)
        Assert.Equal(NativeDisposition.NativeUnknown,final.NativeDisposition);Assert.NotEqual(Phase.Finalized,final.Phase)
        Assert.Equal(2,mechanism.Calls|>List.filter(fun(action,_)->match action with FixedAction.DeleteSecret _ -> true | _ -> false)|>List.length)
        Assert.All(mechanism.Windows,fun(_,window)->Assert.InRange(window.RemainingSeconds,1,90))

    [<Fact>]
    let ``discovery callbacks share one absolute ninety second deadline`` () =
        let listingMechanism=ControlledMechanism("discovery-list-deadline")
        let listingFinal=AttemptOperation.run (operationContext()) (prepared()) listingMechanism
        let listTimeouts=listingMechanism.Calls|>List.choose(fun(action,timeout)->if action=FixedAction.ListRuns then Some timeout else None)
        Assert.Equal<int list>([60;28],listTimeouts)
        Assert.True(listingFinal.OwnedRunId.IsNone)
        let acquisitionMechanism=ControlledMechanism("discovery-acquisition-deadline")
        let acquisitionFinal=AttemptOperation.run (operationContext()) (prepared()) acquisitionMechanism
        Assert.True(acquisitionFinal.OwnedRunId.IsNone)
        Assert.DoesNotContain(acquisitionMechanism.Windows,fun(name,_)->name="ownership")

    [<Fact>]
    let ``native acquisition failure keeps first refusal while cancelling exact owned run and observing retirement`` () =
        let mechanism=ControlledMechanism("exception-after-native")
        let final=AttemptOperation.run (operationContext()) (prepared()) mechanism
        Assert.Equal(Some "mechanism-failure",final.Refusal)
        Assert.True(final.CancellationMayHaveEffect)
        Assert.Equal(1,mechanism.Calls|>List.filter(fun(action,_)->action=FixedAction.CancelOwnedRun "run-1")|>List.length)
        Assert.True(final.RunRetirementObserved)
        Assert.Equal(Phase.Finalized,final.Phase)

    [<Fact>]
    let ``consumed interruption preserves the sole exact owned cancellation attempt`` () =
        let mechanism=ControlledMechanism("signal-after-owned")
        let final=AttemptOperation.run (operationContext()) (prepared()) mechanism
        Assert.Equal(Some "mechanism-failure",final.Refusal)
        Assert.True(final.CancellationMayHaveEffect)
        Assert.Equal(1,mechanism.Calls|>List.filter(fun(action,_)->action=FixedAction.CancelOwnedRun "run-1")|>List.length)
        Assert.True(final.RunRetirementObserved)

    [<Fact>]
    let ``sticky failure cannot cancel a merely listed run`` () =
        let discovery=dispatched()
        let candidate=apply(Observation.RunsObserved(listing discovery ["run-1"]))discovery
        let failed=apply(Observation.OperationFailed OperationFailure.MechanismFailure)candidate|>effectChecked
        let result=AttemptReducer.apply(Observation.EffectRequested(FixedAction.CancelOwnedRun "run-1"))failed
        Assert.Equal(Some "mechanism-failure",result.State.Refusal)
        Assert.False(result.State.CancellationMayHaveEffect)
        Assert.DoesNotContain(FixedAction.CancelOwnedRun "run-1",result.Actions)

module CliTests =
    let sourceRoot () = Environment.GetEnvironmentVariable "HOST_ATTEMPT_RECIPE_ROOT" |> Option.ofObj |> Option.defaultWith(fun()->failwith "HOST_ATTEMPT_RECIPE_ROOT required")
    let bindingDll () = Environment.GetEnvironmentVariable "HOST_ATTEMPT_BINDING_DLL" |> Option.ofObj |> Option.defaultWith(fun()->failwith "HOST_ATTEMPT_BINDING_DLL required")
    let producerDll () = Environment.GetEnvironmentVariable "HOST_ATTEMPT_PRODUCER_DLL" |> Option.ofObj |> Option.defaultWith(fun()->failwith "HOST_ATTEMPT_PRODUCER_DLL required")
    let mutatedProducerDll () = Environment.GetEnvironmentVariable "HOST_ATTEMPT_MUTATED_PRODUCER_DLL" |> Option.ofObj |> Option.defaultWith(fun()->failwith "HOST_ATTEMPT_MUTATED_PRODUCER_DLL required")
    let sourcePins root =
        let native=Path.Combine(root,"deployment/telemetry-collector")
        let entries=["qualify_native.py";"native_producer_support.py";"native-operation-v1.json";"native-producer-config.toml"]
        let value = JsonObject()
        for name in entries do
            value[name] <- JsonValue.Create(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(native,name)))).ToLowerInvariant())
        value.ToJsonString() + "\n"
    let makePlacement root =
        Directory.CreateDirectory root|>ignore
        Directory.CreateDirectory(Path.Combine(root,".github/workflows"))|>ignore
        let workflow=Path.Combine(root,".github/workflows/v2-host-native-private.yml")
        let template=File.ReadAllText(Path.Combine(sourceRoot(),AttemptPreparation.WorkflowTemplateRelativePath))
        let rendered=template.Replace("@@RECIPE_SOURCE_SHA@@",AttemptPreparation.RecipeSha).Replace("@@HOST_BINDING_PROFILE_SHA256@@","1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34")
        let anchor="      - name: Set up exact HOST binding SDK\n"
        let acquisition="      - name: Acquire exact public-only native inputs before authentication\n        env:\n          GITHUB_TOKEN: ${{ github.token }}\n        run: |\n          set -euo pipefail\n          python3 placement/tools/bounded-public-inputs.py acquire \\\n            --repository \"$GITHUB_REPOSITORY\" \\\n            --output \"$GITHUB_WORKSPACE/placement/_private-inputs\"\n          unset GITHUB_TOKEN\n"
        File.WriteAllText(workflow,rendered.Insert(rendered.IndexOf anchor,acquisition))
        run "/usr/bin/git" root ["init";"-q"] |> ignore
        run "/usr/bin/git" root ["config";"user.email";"test@example.invalid"] |> ignore
        run "/usr/bin/git" root ["config";"user.name";"test"] |> ignore
        run "/usr/bin/git" root ["add";"."] |> ignore
        run "/usr/bin/git" root ["commit";"-q";"-m";"fixture"] |> ignore
        let _, head, _ = run "/usr/bin/git" root ["rev-parse";"HEAD"]
        let _, tree, _ = run "/usr/bin/git" root ["rev-parse";"HEAD^{tree}"]
        workflow,head.Trim(),tree.Trim()
    let request placement workflow head tree pins recipeRoot =
        let workflowSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes workflow)).ToLowerInvariant()
        let value = JsonObject()
        let add (name:string) (item:string) = value[name] <- JsonValue.Create item
        add "schema" "fsgg.telemetry.host-attempt-preparation/1"
        add "placementRoot" placement; add "placementSha" head; add "placementTree" tree
        add "workflowPath" ".github/workflows/v2-host-native-private.yml"; add "workflowSha256" workflowSha
        add "qualificationRef" AttemptPreparation.QualificationRef; add "environment" AttemptPreparation.Environment
        add "repository" AttemptPreparation.Repository; add "workflow" AttemptPreparation.Workflow
        value["releaseId"] <- JsonValue.Create(AttemptPreparation.ReleaseId)
        value["manifestAssetId"] <- JsonValue.Create(AttemptPreparation.ManifestAssetId)
        value["archiveAssetId"] <- JsonValue.Create(AttemptPreparation.ArchiveAssetId)
        value["sourceGeneration"] <- JsonValue.Create(1)
        add "recipeRoot" recipeRoot; add "recipeSourceSha" AttemptPreparation.RecipeSha; add "recipeSourceTree" AttemptPreparation.RecipeTree
        add "profilePath" (Path.Combine(recipeRoot,"deployment/telemetry-collector/native-operation-v1.json"))
        add "sourcePinsPath" pins; add "hostBindingDll" (bindingDll());add "mechanismAdapterPath" (Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"..","host_attempt_transport.py"))); add "nonce" "attempt-o-0001"; add "destinationId" "destination-o-0001"
        value["budgetSeconds"] <- JsonValue.Create(2700)
        value.ToJsonString()

    let writeFrame path (value:JsonObject) =
        let payload=Encoding.UTF8.GetBytes(value.ToJsonString())
        let bytes=Array.zeroCreate<byte>(payload.Length+4)
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0,4),payload.Length);payload.CopyTo(bytes,4);File.WriteAllBytes(path,bytes)
    let digestFile path=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes path)).ToLowerInvariant()

    [<Fact>]
    let ``real producer binds exact source and effect check reopens every pinned payload`` () =
        use temp = new TempDirectory()
        let placement = Path.Combine(temp.Path,"placement")
        let workflow, head, tree = makePlacement placement
        let recipe = Path.Combine(temp.Path,"recipe")
        let cloneCode,_,cloneError=run "/usr/bin/git" temp.Path ["clone";"--no-hardlinks";"-q";sourceRoot();recipe]
        Assert.True((cloneCode=0),cloneError)
        let pins = Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins recipe)
        let input = Path.Combine(temp.Path,"request.json")
        let output = Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,request placement workflow head tree pins recipe)
        let code,_,error=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.True((code = 0), error);Assert.Equal("",error);Assert.True(File.Exists output)
        let state=JsonDocument.Parse(File.ReadAllBytes output).RootElement
        let string (name:string)=state.GetProperty(name).GetString()
        let preparedIdentity =
            { SourceGeneration=state.GetProperty("sourceGeneration").GetInt32();PlacementSha=string "placementSha";PlacementTree=string "placementTree";WorkflowSha256=string "workflowSha256"
              RecipeSourceSha=string "recipeSourceSha";RecipeSourceTree=string "recipeSourceTree";ProfileSha256=string "profileSha256";OperationId=string "operationId"
              BindingSha256=string "bindingSha256";BindingProducerSha256=string "bindingProducerSha256";SourcePinsSha256=string "sourcePinsSha256";ProducerSha256=string "producerSha256"
              RuntimeHostSha256=string "runtimeHostSha256";MechanismAdapterSha256=string "mechanismAdapterSha256";Nonce=string "nonce";DestinationId=string "destinationId" }
        let preparation =
            { PlacementRoot=placement;PlacementSha=head;PlacementTree=tree;WorkflowPath=".github/workflows/v2-host-native-private.yml";WorkflowSha256=digestFile workflow
              QualificationRef=AttemptPreparation.QualificationRef;Environment=AttemptPreparation.Environment;Repository=AttemptPreparation.Repository;Workflow=AttemptPreparation.Workflow
              ReleaseId=AttemptPreparation.ReleaseId;ManifestAssetId=AttemptPreparation.ManifestAssetId;ArchiveAssetId=AttemptPreparation.ArchiveAssetId;SourceGeneration=1
              RecipeRoot=recipe;RecipeSourceSha=AttemptPreparation.RecipeSha;RecipeSourceTree=AttemptPreparation.RecipeTree;ProfilePath=Path.Combine(recipe,"deployment/telemetry-collector/native-operation-v1.json")
              SourcePinsPath=pins;HostBindingDll=bindingDll();MechanismAdapterPath=Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"..","host_attempt_transport.py"));Nonce="attempt-o-0001";DestinationId="destination-o-0001";BudgetSeconds=2700 }
        Assert.Equal(AttemptPreparation.RecipeSha,preparedIdentity.RecipeSourceSha);Assert.Equal(AttemptPreparation.OperationId,preparedIdentity.OperationId)
        Assert.Equal("1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34",preparedIdentity.ProfileSha256)
        AttemptPreparation.revalidateWithin preparedIdentity preparation 10000
        let role="deployment/telemetry-collector/native_producer_support.py"
        let assumeCode,_,assumeError=run "/usr/bin/git" recipe ["update-index";"--assume-unchanged";role]
        Assert.True((assumeCode=0),assumeError)
        File.AppendAllText(Path.Combine(recipe,role),"\n# controlled payload-byte mutation\n")
        let statusCode,status,statusError=run "/usr/bin/git" recipe ["status";"--porcelain"]
        Assert.True((statusCode=0),statusError);Assert.Equal("",status)
        let refusalCode =
            try AttemptPreparation.revalidateWithin preparedIdentity preparation 10000; "not-refused"
            with AttemptRefusal value->value
        Assert.Equal("source-pin-content-refused",refusalCode)

    [<Fact>]
    let ``changed profile is refused before state or effect output`` () =
        use temp = new TempDirectory()
        let placement = Path.Combine(temp.Path,"placement")
        let workflow, head, tree = makePlacement placement
        let clone = Path.Combine(temp.Path,"recipe")
        let code, _, _ = run "/usr/bin/git" temp.Path ["clone";"--no-hardlinks";"-q";sourceRoot();clone]
        Assert.Equal(0,code)
        File.WriteAllText(Path.Combine(clone,"deployment/telemetry-collector/native-operation-v1.json"),"{}")
        let pins=Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins clone)
        let text=request placement workflow head tree pins clone
        let input=Path.Combine(temp.Path,"request.json")
        let output=Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,text)
        let exitCode,_,_=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,exitCode);Assert.False(File.Exists output)

    [<Fact>]
    let ``workflow mutation after snapshot is refused before output`` () =
        use temp = new TempDirectory()
        let placement = Path.Combine(temp.Path,"placement")
        let workflow, head, tree = makePlacement placement
        let pins=Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins(sourceRoot()))
        let input=Path.Combine(temp.Path,"request.json")
        let output=Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,request placement workflow head tree pins (sourceRoot()))
        File.AppendAllText(workflow,"# changed\n")
        let code,_,_=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,code);Assert.False(File.Exists output)

    [<Fact>]
    let ``markers in comments cannot replace the authoritative rendered workflow`` () =
        use temp = new TempDirectory()
        let placement=Path.Combine(temp.Path,"placement")
        let workflow,_,_=makePlacement placement
        File.WriteAllText(workflow,String.concat "\n" ["name: unrelated";"on: workflow_dispatch";"jobs:";"  noop:";"    runs-on: ubuntu-latest";"    steps:";"      - run: echo synthetic";"# "+AttemptPreparation.RecipeSha;"# 1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34";"# "+AttemptPreparation.OperationId;"# "+AttemptPreparation.QualificationRef;"# "+AttemptPreparation.Environment;"# FSGG_V2_HOST_018_EFFECT_ADMISSION";""])
        run "/usr/bin/git" placement ["add";"."]|>ignore
        run "/usr/bin/git" placement ["commit";"-q";"-m";"wrong workflow"]|>ignore
        let _,head,_=run "/usr/bin/git" placement ["rev-parse";"HEAD"]
        let _,tree,_=run "/usr/bin/git" placement ["rev-parse";"HEAD^{tree}"]
        let pins=Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins(sourceRoot()))
        let input=Path.Combine(temp.Path,"request.json")
        let output=Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,request placement workflow (head.Trim()) (tree.Trim()) pins (sourceRoot()))
        let code,_,_=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,code);Assert.False(File.Exists output)

    [<Fact>]
    let ``changed source pin is refused by real HostBinding before output`` () =
        use temp = new TempDirectory()
        let placement = Path.Combine(temp.Path,"placement")
        let workflow, head, tree = makePlacement placement
        let pins=Path.Combine(temp.Path,"source-pins.json")
        let values=JsonNode.Parse(sourcePins(sourceRoot())).AsObject()
        values["qualify_native.py"] <- JsonValue.Create(String.replicate 64 "0")
        File.WriteAllText(pins,values.ToJsonString()+"\n")
        let input=Path.Combine(temp.Path,"request.json")
        let output=Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,request placement workflow head tree pins (sourceRoot()))
        let code,_,_=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,code);Assert.False(File.Exists output)

    [<Fact>]
    let ``wrong binding executable is refused before output`` () =
        use temp = new TempDirectory()
        let placement = Path.Combine(temp.Path,"placement")
        let workflow, head, tree = makePlacement placement
        let pins=Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins(sourceRoot()))
        let values=JsonNode.Parse(request placement workflow head tree pins (sourceRoot())).AsObject()
        values["hostBindingDll"] <- JsonValue.Create(producerDll())
        let input=Path.Combine(temp.Path,"request.json")
        let output=Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,values.ToJsonString())
        let code,_,error=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,code);Assert.False(File.Exists output)
        Assert.Contains("host-attempt-diagnostic:",error)
        Assert.Contains("\"stage\":\"preparation-render\"",error)
        Assert.Contains("\"outcome\":\"returned\"",error)
        Assert.DoesNotContain("hostBindingDll",error)

    [<Fact>]
    let ``duplicate preparation key is refused closed`` () =
        use temp = new TempDirectory()
        let input = Path.Combine(temp.Path,"request.json")
        let output = Path.Combine(temp.Path,"state.json")
        File.WriteAllText(input,"{\"schema\":\"fsgg.telemetry.host-attempt-preparation/1\",\"schema\":\"changed\"}")
        let code,_,_=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";input;"--output";output]
        Assert.Equal(2,code);Assert.False(File.Exists output)

    [<Fact>]
    let ``compiled run uses raw HTTP separate ownership verifier channels and persisted readback`` () =
        use temp=new TempDirectory()
        File.SetUnixFileMode(temp.Path,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        let placement=Path.Combine(temp.Path,"placement")
        let workflow,head,tree=makePlacement placement
        let pins=Path.Combine(temp.Path,"source-pins.json")
        File.WriteAllText(pins,sourcePins(sourceRoot()))
        let prep=JsonNode.Parse(request placement workflow head tree pins (sourceRoot())).AsObject()
        let adapter=Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"..","host_attempt_transport.py"))
        let invocation="invocation-positive-1"
        let wrapper=JsonObject()
        wrapper["schema"]<-JsonValue.Create("fsgg.telemetry.host-attempt-run-request/1")
        wrapper["preparation"]<-prep.DeepClone()
        wrapper["producerSha256"]<-JsonValue.Create(digestFile(producerDll()))
        wrapper["transportSha256"]<-JsonValue.Create(digestFile adapter)
        wrapper["invocationId"]<-JsonValue.Create invocation
        wrapper["leaseId"]<-JsonValue.Create("lease-positive-1")
        wrapper["authChannel"]<-JsonValue.Create("auth-metadata-v1")
        wrapper["ownershipChannel"]<-JsonValue.Create("owned-run-v1")
        wrapper["verifierChannel"]<-JsonValue.Create("native-verifier-v1")
        let input=Path.Combine(temp.Path,"run.json")
        File.WriteAllText(input,wrapper.ToJsonString())
        let prepareInput=Path.Combine(temp.Path,"prepare.json")
        let prepareOutput=Path.Combine(temp.Path,"prepared.json")
        File.WriteAllText(prepareInput,prep.ToJsonString())
        let prepareCode,_,prepareError=run "/usr/bin/dotnet" temp.Path [producerDll();"prepare";"--request";prepareInput;"--output";prepareOutput]
        Assert.True((prepareCode=0),prepareError)
        let preparedNode=JsonNode.Parse(File.ReadAllText(prepareOutput)).AsObject()
        let bindingSha=preparedNode["bindingSha256"].GetValue<string>()
        let auth=JsonObject()
        for name,value in ["schema","fsgg.telemetry.host-attempt-auth-metadata/1";"invocationId",invocation;"destinationId","destination-o-0001";"repository",AttemptPreparation.Repository;"environment",AttemptPreparation.Environment;"requestedRole","native-auth"] do auth[name]<-JsonValue.Create(value)
        auth["sequence"]<-JsonValue.Create(2)
        let ownership=JsonObject()
        for name,value in ["schema","fsgg.telemetry.host-attempt-owned-run/1";"invocationId",invocation;"destinationId","destination-o-0001";"runId","77";"repository",AttemptPreparation.Repository;"workflow",AttemptPreparation.Workflow;"qualificationRef",AttemptPreparation.QualificationRef;"placementSha",head;"nonce","attempt-o-0001";"event","workflow_dispatch"] do ownership[name]<-JsonValue.Create(value)
        ownership["sequence"]<-JsonValue.Create(11)
        ownership["runAttempt"]<-JsonValue.Create(1)
        let native=JsonObject()
        for name,value in ["schema","fsgg.telemetry.native-operation-result/1";"status","qualified";"operationId",AttemptPreparation.OperationId;"runNonce","attempt-o-0001";"parentThreadId","11111111-1111-1111-1111-111111111111";"childThreadId","22222222-2222-2222-2222-222222222222";"nativeAgent","child_agent";"nativeAgentPath","agents/child_agent.toml";"protocolMode","v2";"originalRolloutAudit","bounded-complete";"childTerminalEvidence","wait-history+event"] do native[name]<-JsonValue.Create(value)
        for name,value in ["spawnCount",1;"childTerminalTurns",1;"waitCount",1;"followups",0;"automaticRetries",0] do native[name]<-JsonValue.Create(value)
        native["observedNotifications"]<-JsonNode.Parse("{\"childAcknowledgements\":1,\"parentAcknowledgements\":1,\"childTerminal\":1}")
        native["authoritativeHistory"]<-JsonNode.Parse("{\"parentSpawn\":1,\"parentWait\":1,\"parentAcknowledgements\":1,\"childAcknowledgements\":1,\"prohibitedTools\":0,\"followups\":0}")
        native["events"]<-JsonNode.Parse("[{\"sequence\":1,\"kind\":\"actual-v2-completed-activity\"}]")
        let nativeBytes=Encoding.UTF8.GetBytes(native.ToJsonString())
        AttemptAcquisition.canonicalNative AttemptPreparation.OperationId "attempt-o-0001" nativeBytes
        let malformed=native.DeepClone().AsObject()
        malformed["parentThreadId"]<-null
        Assert.Throws<AttemptRefusal>(fun()->AttemptAcquisition.canonicalNative AttemptPreparation.OperationId "attempt-o-0001" (Encoding.UTF8.GetBytes(malformed.ToJsonString()))|>ignore)|>ignore
        let verifier=JsonObject()
        for name,value in ["schema","fsgg.telemetry.host-attempt-verifier-receipt/1";"invocationId",invocation;"destinationId","destination-o-0001";"runId","77";"nonce","attempt-o-0001";"profileSha256","1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34";"bindingSha256",bindingSha;"sourceSha",head;"artifactName","private-native-qualification-77-1";"resultBase64",Convert.ToBase64String nativeBytes;"resultSha256",Convert.ToHexString(SHA256.HashData nativeBytes).ToLowerInvariant();"verifierReceiptSha256",String.replicate 64 "9"] do verifier[name]<-JsonValue.Create(value)
        verifier["sequence"]<-JsonValue.Create(13)
        verifier["runAttempt"]<-JsonValue.Create(1)
        verifier["verifierExitCode"]<-JsonValue.Create(0)
        let custodyBytes=Encoding.UTF8.GetBytes("synthetic-canonical-verifier-receipt")
        verifier["verifierReceiptBase64"]<-JsonValue.Create(Convert.ToBase64String custodyBytes)
        verifier["verifierReceiptSha256"]<-JsonValue.Create(Convert.ToHexString(SHA256.HashData custodyBytes).ToLowerInvariant())
        let authPath=Path.Combine(temp.Path,"auth.frame")
        let ownershipPath=Path.Combine(temp.Path,"ownership.frame")
        let verifierPath=Path.Combine(temp.Path,"verifier.frame")
        writeFrame authPath auth
        writeFrame ownershipPath ownership
        writeFrame verifierPath verifier
        let stub=Path.Combine(temp.Path,"gh")
        let stubSource="""#!/usr/bin/python3
import json,os,sys
a=sys.argv[1:]; state=os.environ['STUB_STATE']
def http(body): print('HTTP/1.1 200 OK\r\ndate: Fri, 02 Oct 2026 00:00:00 GMT\r\ncontent-type: application/json\r\n\r\n'+json.dumps(body,separators=(',',':')))
if a and a[0]=='api':
 e=a[-1]
 if '/workflows/' in e:
  n=int(open(state).read()) if os.path.exists(state) else 0;open(state,'w').write(str(n+1));runs=[] if n==0 else [{'id':77,'head_sha':os.environ['PLACEMENT_SHA'],'event':'workflow_dispatch','status':'completed','conclusion':'success','created_at':'2026-10-02T00:00:01Z','run_attempt':1}];http({'total_count':len(runs),'workflow_runs':runs})
 elif '/actions/runs/' in e:
  p=state+'.get';n=int(open(p).read()) if os.path.exists(p) else 0;open(p,'w').write(str(n+1));http({'id':999 if os.environ.get('MUTATE_RUN_ID')=='1' and n==1 else 77,'head_sha':os.environ['PLACEMENT_SHA'],'event':'workflow_dispatch','status':'completed','conclusion':'success','run_attempt':1})
 elif '/secrets?' in e:http({'total_count':0,'secrets':[]})
 else:http({'full_name':'FS-GG/FS.GG.GitHub.Substrate.Sandbox'})
elif a[:2]==['secret','set']:
 data=sys.stdin.read();assert data and data!='-';assert '--body' not in a
elif a and a[0] in ('secret','workflow','run'): pass
else: raise SystemExit(2)
"""
        File.WriteAllText(stub,stubSource)
        File.SetUnixFileMode(stub,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        let output=Path.Combine(temp.Path,"final.json")
        let command=$"exec 3<{authPath}; exec 4<{ownershipPath}; exec 5<{verifierPath}; exec /usr/bin/dotnet {producerDll()} run --request {input} --output {output}"
        let start=ProcessStartInfo("/usr/bin/bash",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp.Path)
        start.ArgumentList.Add "-c"
        start.ArgumentList.Add command
        start.Environment["HOST_ATTEMPT_AUTH_FD"]<-"3";start.Environment["HOST_ATTEMPT_OWNERSHIP_FD"]<-"4";start.Environment["HOST_ATTEMPT_VERIFIER_FD"]<-"5";start.Environment["HOST_ATTEMPT_LEASE_ROOT"]<-temp.Path;start.Environment["FSGG_V2_HOST_018_NATIVE_AUTH_JSON_B64"]<-"synthetic-native-auth";start.Environment["PATH"]<-temp.Path+":/usr/bin:/bin";start.Environment["STUB_STATE"]<-Path.Combine(temp.Path,"stub-state");start.Environment["PLACEMENT_SHA"]<-head
        use child=Process.Start start
        let stdout=child.StandardOutput.ReadToEnd()
        let stderr=child.StandardError.ReadToEnd()
        child.WaitForExit()
        Assert.True(child.ExitCode=0,$"stdout={stdout} stderr={stderr}")
        let final=JsonNode.Parse(File.ReadAllText output).AsObject()
        Assert.True(final["phase"].GetValue<string>()="finalized",final.ToJsonString()+" stderr="+stderr);Assert.True(final["nativeDisposition"].GetValue<string>()="accepted",final.ToJsonString()+" stderr="+stderr);Assert.Equal("secrets-absent",final["cleanupDisposition"].GetValue<string>())
        let readback=JsonNode.Parse(File.ReadAllText(Path.Combine(temp.Path,"lease-positive-1.readback.json"))).AsObject()
        Assert.Equal(final["phase"].GetValue<string>(),readback["phase"].GetValue<string>());Assert.Equal(final["nativeDisposition"].GetValue<string>(),readback["nativeDisposition"].GetValue<string>())
        match Environment.GetEnvironmentVariable "HOST_ATTEMPT_ACQUIRED_TRACE_OUTPUT" with
        | null|"" -> ()
        | trace -> File.Copy(Path.Combine(temp.Path,"lease-positive-1.trace.json"),trace,false);File.SetUnixFileMode(trace,UnixFileMode.UserRead|||UnixFileMode.UserWrite)
        wrapper["leaseId"]<-JsonValue.Create("lease-identity-mutation-1")
        let mutationInput=Path.Combine(temp.Path,"mutation-run.json")
        let mutationOutput=Path.Combine(temp.Path,"mutation-final.json")
        File.WriteAllText(mutationInput,wrapper.ToJsonString())
        let mutationCommand=$"exec 3<{authPath}; exec 4<{ownershipPath}; exec 5<{verifierPath}; exec /usr/bin/dotnet {producerDll()} run --request {mutationInput} --output {mutationOutput}"
        let mutationStart=ProcessStartInfo("/usr/bin/bash",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp.Path)
        mutationStart.ArgumentList.Add "-c";mutationStart.ArgumentList.Add mutationCommand
        for KeyValue(key,value) in start.Environment do mutationStart.Environment[key]<-value
        mutationStart.Environment["STUB_STATE"]<-Path.Combine(temp.Path,"mutation-stub-state")
        mutationStart.Environment["MUTATE_RUN_ID"]<-"1"
        use mutationChild=Process.Start mutationStart
        let mutationStdout=mutationChild.StandardOutput.ReadToEnd()
        let mutationStderr=mutationChild.StandardError.ReadToEnd()
        mutationChild.WaitForExit()
        Assert.True(mutationChild.ExitCode=0,$"stdout={mutationStdout} stderr={mutationStderr}")
        let mutation=JsonNode.Parse(File.ReadAllText mutationOutput).AsObject()
        Assert.Equal("mechanism-failure",mutation["refusal"].GetValue<string>())
        Assert.Equal("unknown",mutation["nativeDisposition"].GetValue<string>())
        wrapper["producerSha256"]<-JsonValue.Create(digestFile(mutatedProducerDll()))
        wrapper["leaseId"]<-JsonValue.Create("lease-guard-removed-1")
        let removedInput=Path.Combine(temp.Path,"removed-run.json")
        let removedOutput=Path.Combine(temp.Path,"removed-final.json")
        File.WriteAllText(removedInput,wrapper.ToJsonString())
        let removedCommand=$"exec 3<{authPath}; exec 4<{ownershipPath}; exec 5<{verifierPath}; exec /usr/bin/dotnet {mutatedProducerDll()} run --request {removedInput} --output {removedOutput}"
        let removedStart=ProcessStartInfo("/usr/bin/bash",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp.Path)
        removedStart.ArgumentList.Add "-c";removedStart.ArgumentList.Add removedCommand
        for KeyValue(key,value) in start.Environment do removedStart.Environment[key]<-value
        removedStart.Environment["STUB_STATE"]<-Path.Combine(temp.Path,"removed-stub-state")
        removedStart.Environment["MUTATE_RUN_ID"]<-"1"
        use removedChild=Process.Start removedStart
        let removedStdout=removedChild.StandardOutput.ReadToEnd()
        let removedStderr=removedChild.StandardError.ReadToEnd()
        removedChild.WaitForExit()
        Assert.True(removedChild.ExitCode=0,$"stdout={removedStdout} stderr={removedStderr}")
        let removed=JsonNode.Parse(File.ReadAllText removedOutput).AsObject()
        Assert.Equal("accepted",removed["nativeDisposition"].GetValue<string>())
        Assert.Null(removed["refusal"])
        wrapper["producerSha256"]<-JsonValue.Create(digestFile(producerDll()))
        wrapper["transportSha256"]<-JsonValue.Create(String.replicate 64 "0")
        wrapper["leaseId"]<-JsonValue.Create("lease-guard-1")
        let guardInput=Path.Combine(temp.Path,"guard-run.json")
        let guardOutput=Path.Combine(temp.Path,"guard-final.json")
        File.WriteAllText(guardInput,wrapper.ToJsonString())
        let guardCommand=$"exec 3<{authPath}; exec 4<{ownershipPath}; exec 5<{verifierPath}; exec /usr/bin/dotnet {producerDll()} run --request {guardInput} --output {guardOutput}"
        let guardStart=ProcessStartInfo("/usr/bin/bash",UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp.Path)
        guardStart.ArgumentList.Add "-c"
        guardStart.ArgumentList.Add guardCommand
        for KeyValue(key,value) in start.Environment do guardStart.Environment[key]<-value
        use guardChild=Process.Start guardStart
        let guardStdout=guardChild.StandardOutput.ReadToEnd()
        let guardStderr=guardChild.StandardError.ReadToEnd()
        guardChild.WaitForExit()
        Assert.True(guardChild.ExitCode=0,$"stdout={guardStdout} stderr={guardStderr}")
        let guarded=JsonNode.Parse(File.ReadAllText guardOutput).AsObject()
        Assert.Equal("mechanism-failure",guarded["refusal"].GetValue<string>())
        Assert.Equal(0,guarded["secretIntentions"].AsArray().Count)
        Assert.Empty(Directory.GetFiles(temp.Path,"lease-guard-1*.intention.json"))
