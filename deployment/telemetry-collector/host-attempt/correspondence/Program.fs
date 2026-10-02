open System
open System.IO
open System.Security.Cryptography
open System.Text.Json.Nodes
open Fs.Gg.Telemetry.HostAttempt
open FsQuint
let fail message=raise(InvalidOperationException message)
let replay=function Ok value->value|Error error->fail $"FsQuint: %A{error}"
let sha path=File.ReadAllBytes path|>SHA256.HashData|>Convert.ToHexString|>fun value->value.ToLowerInvariant()
let integer value=QuintReplayValue.Integer(string value)
let text value=QuintReplayValue.Text value
let boolean value=QuintReplayValue.Boolean value
let required name=Environment.GetEnvironmentVariable name|>Option.ofObj|>Option.filter(File.Exists)|>Option.defaultWith(fun()->fail(name+" required"))
let model=required "HOST_ATTEMPT_MODEL"
let itfRoot=Environment.GetEnvironmentVariable "HOST_ATTEMPT_ITF_ROOT"
if String.IsNullOrWhiteSpace itfRoot then fail "HOST_ATTEMPT_ITF_ROOT required"
let profilePath=required "HOST_ATTEMPT_PROFILE"
let adapterPath=required "HOST_ATTEMPT_ADAPTER"
let productionPath=required "HOST_ATTEMPT_PRODUCTION_DLL"
let toolPath=required "HOST_ATTEMPT_QUINT_TOOL"
let acquiredPath=required "HOST_ATTEMPT_ACQUIRED_STATE"
let loadedProductionPath=typeof<AttemptState>.Assembly.Location
if sha loadedProductionPath<>sha productionPath then fail $"loaded production mismatch: loaded={sha loadedProductionPath} provided={sha productionPath}"
let acquiredTrace=JsonNode.Parse(File.ReadAllText acquiredPath).AsObject()
if acquiredTrace["schema"].GetValue<string>()<>"fsgg.telemetry.host-attempt-acquired-trace/1"||acquiredTrace["producerSha256"].GetValue<string>()<>sha productionPath then fail "concrete acquired trace identity differs"
let acquired=acquiredTrace["finalState"].AsObject()
let acquiredText (name:string)=acquired[name].GetValue<string>()
if acquiredText "schema"<>"fsgg.telemetry.host-attempt-state/2"||acquiredText "phase"<>"finalized"||acquiredText "nativeDisposition"<>"accepted"||acquiredText "cleanupDisposition"<>"secrets-absent"||not(acquired["runRetirementObserved"].GetValue<bool>())||acquired["ownedRunId"].GetValue<string>()=""||not(acquired["dispatchAcknowledged"].GetValue<bool>())||not(isNull acquired["refusal"])||acquiredText "producerSha256"<>sha productionPath then fail "concrete acquired production trace did not join canonical success terminal"
let initial()=
 let identity={SourceGeneration=1;PlacementSha=String.replicate 40 "a";PlacementTree=String.replicate 40 "b";WorkflowSha256=String.replicate 64 "c";RecipeSourceSha=AttemptPreparation.RecipeSha;RecipeSourceTree=AttemptPreparation.RecipeTree;ProfileSha256=sha profilePath;OperationId=AttemptPreparation.OperationId;BindingSha256=String.replicate 64 "e";BindingProducerSha256=String.replicate 64 "f";SourcePinsSha256=String.replicate 64 "1";ProducerSha256=sha productionPath;RuntimeHostSha256=sha "/usr/bin/dotnet";MechanismAdapterSha256=sha adapterPath;Nonce="attempt-o-correspondence";DestinationId="destination-correspondence"}
 {Schema="fsgg.telemetry.host-attempt-state/2";Phase=Phase.Prepared;Prepared=identity;CurrentSourceGeneration=1;CurrentBindingSha256=identity.BindingSha256;ElapsedSeconds=0;BudgetSeconds=2700;DispatchIntended=false;DispatchMayHaveEffect=false;DispatchAcknowledged=false;EffectCheckFresh=false;SecretIntentions=Set.empty;SecretAcknowledgments=Set.empty;SecretsMayHaveEffect=Set.empty;SecretAbsenceObserved=Set.empty;CandidateRuns=[];OwnedRunId=None;CancellationMayHaveEffect=false;RunRetirementObserved=false;NativeDisposition=NativeDisposition.NativeUnknown;CleanupDisposition=CleanupDisposition.CleanupUnknown;Refusal=None}
let phase state=ClosedNames.phase state.Phase|>fun value->value.Split('-')|>Array.map(fun part->part[0].ToString().ToUpperInvariant()+part.Substring(1))|>String.concat ""
let refusal state=match state.Refusal with None->"None"|Some "effect-acknowledgment-refused"->"EffectAck"|Some "effect-action-refused"->"EffectAction"|Some "source-invalidated"->"SourceInvalid"|Some "cancel-run-refused"->"CancelRun"|Some "dispatch-repeat-refused"->"DispatchRepeat"|Some "mechanism-failure"->"MechanismFailure"|Some _->"Other"
let project state=
 let has role values=Set.contains role values
 let cleanup=match state.CleanupDisposition with CleanupDisposition.CleanupUnknown->"Unknown"|CleanupDisposition.CleanupPending->"Pending"|CleanupDisposition.SecretsAbsent->"SecretsAbsent"
 let native=match state.NativeDisposition with NativeDisposition.NativeUnknown->"Unknown"|NativeDisposition.NativeAccepted->"Accepted"|NativeDisposition.NativeRefused->"Refused"
 let abstractElapsed=min 4 ((state.ElapsedSeconds+674)/675)
 let fields=["phase",text(phase state);"current",boolean(state.CurrentSourceGeneration=state.Prepared.SourceGeneration&&state.CurrentBindingSha256=state.Prepared.BindingSha256);"checkFresh",boolean state.EffectCheckFresh;"refusal",text(refusal state);"authIntent",boolean(has SecretRole.NativeAuth state.SecretIntentions);"authAck",boolean(has SecretRole.NativeAuth state.SecretAcknowledgments);"admissionIntent",boolean(has SecretRole.EffectAdmission state.SecretIntentions);"admissionAck",boolean(has SecretRole.EffectAdmission state.SecretAcknowledgments);"dispatchIntentions",integer(if state.DispatchIntended then 1 else 0);"dispatchAck",boolean state.DispatchAcknowledged;"dispatchMayHaveEffect",boolean state.DispatchMayHaveEffect;"candidateCount",integer state.CandidateRuns.Length;"ownedRun",integer(if state.OwnedRunId.IsSome then 1 else 0);"cancellationMayHaveEffect",boolean state.CancellationMayHaveEffect;"native",text native;"secretsAbsent",integer state.SecretAbsenceObserved.Count;"runRetired",boolean state.RunRetirementObserved;"cleanup",text cleanup;"elapsed",integer abstractElapsed]
 let draft:QuintReplayState={Identity=String.replicate 64 "0";Bindings=["state",QuintReplayValue.Record fields]}
 {draft with Identity=QuintReplay.stateFingerprint draft|>replay}
let projectJson (state:JsonObject)=
 let textValue (name:string)=state[name].GetValue<string>()
 let strings (name:string)=state[name].AsArray()|>Seq.map _.GetValue<string>()|>Set.ofSeq
 let phaseValue=textValue "phase"|>fun (value:string)->value.Split('-')|>Array.map(fun (part:string)->part[0].ToString().ToUpperInvariant()+part.Substring(1))|>String.concat ""
 let refusalValue=if isNull state["refusal"] then "None" else match textValue "refusal" with|"effect-acknowledgment-refused"->"EffectAck"|"effect-action-refused"->"EffectAction"|"source-invalidated"->"SourceInvalid"|"cancel-run-refused"->"CancelRun"|"dispatch-repeat-refused"->"DispatchRepeat"|"mechanism-failure"->"MechanismFailure"|_->"Other"
 let cleanupValue=match textValue "cleanupDisposition" with|"unknown"->"Unknown"|"pending"->"Pending"|_->"SecretsAbsent"
 let nativeValue=match textValue "nativeDisposition" with|"unknown"->"Unknown"|"accepted"->"Accepted"|_->"Refused"
 let intentions=strings "secretIntentions"
 let acknowledgments=strings "secretAcknowledgments"
 let elapsed=min 4 (state["elapsedSeconds"].GetValue<int>()/675)
 let fields=["phase",text phaseValue;"current",boolean(state["currentSourceGeneration"].GetValue<int>()=state["sourceGeneration"].GetValue<int>()&&textValue "currentBindingSha256"=textValue "bindingSha256");"checkFresh",boolean(state["effectCheckFresh"].GetValue<bool>());"refusal",text refusalValue;"authIntent",boolean(intentions.Contains "native-auth");"authAck",boolean(acknowledgments.Contains "native-auth");"admissionIntent",boolean(intentions.Contains "effect-admission");"admissionAck",boolean(acknowledgments.Contains "effect-admission");"dispatchIntentions",integer(if state["dispatchIntended"].GetValue<bool>() then 1 else 0);"dispatchAck",boolean(state["dispatchAcknowledged"].GetValue<bool>());"dispatchMayHaveEffect",boolean(state["dispatchMayHaveEffect"].GetValue<bool>());"candidateCount",integer(state["candidateRuns"].AsArray().Count);"ownedRun",integer(if isNull state["ownedRunId"] then 0 else 1);"cancellationMayHaveEffect",boolean(state["cancellationMayHaveEffect"].GetValue<bool>());"native",text nativeValue;"secretsAbsent",integer(state["secretAbsenceObserved"].AsArray().Count);"runRetired",boolean(state["runRetirementObserved"].GetValue<bool>());"cleanup",text cleanupValue;"elapsed",integer elapsed]
 let draft:QuintReplayState={Identity=String.replicate 64 "0";Bindings=["state",QuintReplayValue.Record fields]}
 {draft with Identity=QuintReplay.stateFingerprint draft|>replay}
let baseCases=[
 "success",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverOne;AttemptReducer.OwnRun;AttemptReducer.AcceptNative;AttemptReducer.ObserveAuthAbsent;AttemptReducer.ObserveAdmissionAbsent;AttemptReducer.RetireRun],["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"ackDispatch";"discoverOne";"ownRun";"acceptNative";"observeSecretAbsent";"observeSecretAbsent";"retireRun"]
 "ambiguous",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverTwo],["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"ackDispatch";"discoverTwo"]
 "lost",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.LoseDispatch],["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"loseDispatch"]
 "sticky",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.AckAuth],["place";"checkEffect";"requestAuth";"ackAuth";"duplicateAckAuth"]
 "refused-owned-cancel",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverOne;AttemptReducer.OwnRun;AttemptReducer.FailOperation;AttemptReducer.CheckEffect;AttemptReducer.RequestCancel;AttemptReducer.AckCancel],["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"ackDispatch";"discoverOne";"ownRun";"failOwnedNative";"checkCleanupEffect";"requestCleanupCancel";"ackCleanupCancel"]
 "false-ownership",[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverOne;AttemptReducer.CheckEffect;AttemptReducer.RequestCancel],["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"ackDispatch";"discoverOne";"checkEffect";"cancelWithoutOwnership"]]
let deadlineActions=[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverOne;AttemptReducer.OwnRun;AttemptReducer.AcceptNative;AttemptReducer.ObserveAuthAbsent;AttemptReducer.ObserveAdmissionAbsent;AttemptReducer.RetireRun;AttemptReducer.AdvanceToDeadline;AttemptReducer.AdvanceToDeadline;AttemptReducer.AdvanceToDeadline;AttemptReducer.AdvanceToDeadline]
let cases=baseCases@[("deadline",deadlineActions,["place";"checkEffect";"requestAuth";"ackAuth";"checkEffect";"requestAdmission";"ackAdmission";"checkEffect";"requestDispatch";"ackDispatch";"discoverOne";"ownRun";"acceptNative";"observeSecretAbsent";"observeSecretAbsent";"retireRun";"advanceTime";"advanceTime";"advanceTime";"advanceTime"])]
let environment steps={Seed="20261004";Bounds=["steps",int64 steps];ToolFingerprint=sha toolPath;ProfileFingerprint=sha profilePath;ContractFingerprint=sha model;AdapterFingerprint=sha adapterPath;ImplementationFingerprint=sha productionPath}
let mutable transitions=0
for scenario,actions,names in cases do
 let states=actions|>List.scan(fun state action->AttemptReducer.applyModelAction action state)(initial())|>List.map project
 let observations=List.map3(fun index name actual->{Index=index;Action=name;Source={Path="HostAttempt.qnt";Line=1;Column=index};Actual=actual})[1..names.Length] names states.Tail
 let context={Environment=environment names.Length;Steps=observations|>List.map(fun item->{Index=item.Index;Action=item.Action;Source=item.Source})}
 let root=JsonNode.Parse(File.ReadAllText(Path.Combine(itfRoot,$"{scenario}-0.itf.json"))).AsObject()
 let metadata=root["#meta"].AsObject()
 metadata.Remove("description")|>ignore
 metadata.Remove("timestamp")|>ignore
 let trace=QuintReplay.decodeItf context (root.ToJsonString())|>replay
 match QuintReplay.compare trace observations with|Ok QuintReplayResult.Equivalent->()|value->fail $"{scenario} correspondence failed: %A{value}"
 transitions<-transitions+names.Length
let _,_,successNames=cases|>List.find(fun(name,_,_)->name="success")
let acquiredSteps=acquiredTrace["steps"].AsArray()|>Seq.map _.AsObject()|>Seq.toList
let acquiredNames=acquiredSteps|>List.map(fun step->step["action"].GetValue<string>())
if acquiredNames<>successNames then fail $"concrete acquired action trace differs: %A{acquiredNames}"
let acquiredStates=acquiredSteps|>List.map(fun step->projectJson(step["state"].AsObject()))
let acquiredObservations=List.map3(fun index name actual->{Index=index;Action=name;Source={Path="HostAttempt.qnt";Line=1;Column=index};Actual=actual})[1..acquiredNames.Length] acquiredNames acquiredStates
let acquiredContext={Environment=environment acquiredNames.Length;Steps=acquiredObservations|>List.map(fun item->{Index=item.Index;Action=item.Action;Source=item.Source})}
let successRoot=JsonNode.Parse(File.ReadAllText(Path.Combine(itfRoot,"success-0.itf.json"))).AsObject()
let successMetadata=successRoot["#meta"].AsObject()
successMetadata.Remove("description")|>ignore
successMetadata.Remove("timestamp")|>ignore
let acquiredCanonical=QuintReplay.decodeItf acquiredContext (successRoot.ToJsonString())|>replay
match QuintReplay.compare acquiredCanonical acquiredObservations with|Ok QuintReplayResult.Equivalent->()|value->fail $"concrete acquired transition correspondence failed: %A{value}"
let reduce action state=AttemptReducer.applyModelAction action state
let watching=[AttemptReducer.Prepare;AttemptReducer.CheckEffect;AttemptReducer.RequestAuth;AttemptReducer.AckAuth;AttemptReducer.CheckEffect;AttemptReducer.RequestAdmission;AttemptReducer.AckAdmission;AttemptReducer.CheckEffect;AttemptReducer.RequestDispatch;AttemptReducer.AckDispatch;AttemptReducer.DiscoverOne;AttemptReducer.OwnRun]|>List.fold(fun state action->reduce action state)(initial())
let cancelled=watching|>reduce AttemptReducer.CheckEffect|>reduce AttemptReducer.RequestCancel
let guarded=(AttemptReducer.applyNativeEvidenceMutationForCorrespondence false cancelled).State
let guardRemoved=(AttemptReducer.applyNativeEvidenceMutationForCorrespondence true cancelled).State
if guarded.NativeDisposition<>NativeDisposition.NativeUnknown||guardRemoved.NativeDisposition<>NativeDisposition.NativeAccepted||project guarded=project guardRemoved then fail "actual native cancellation guard mutation did not diverge"
printfn "HostAttempt canonical action/actual reducer correspondence passed: %d retained scenarios, %d retained transitions; loaded=%s; 16-step concrete acquired trace matched canonical success ITF; actual cancellation guard mutation diverged" cases.Length transitions (sha loadedProductionPath)
