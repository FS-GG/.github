open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open FSGG.Telemetry.PersistentV3.ImageClosure
open FsQuint

let fail message=raise(InvalidOperationException message)
let replay=function Ok value->value|Error error->fail $"FsQuint: %A{error}"
let root=Environment.GetEnvironmentVariable "PERSISTENT_V3_RUNNER_ITF_ROOT"
if String.IsNullOrWhiteSpace root||not(Directory.Exists root)then fail "PERSISTENT_V3_RUNNER_ITF_ROOT required"
let text value=QuintReplayValue.Text value
let boolean value=QuintReplayValue.Boolean value
let fingerprint (value:string)=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes value)).ToLowerInvariant()
let phase=function Prepared->"Prepared"|Acquired->"Acquired"|Validated->"Validated"|CreatingStores->"CreatingStores"|BuildingFirst->"BuildingFirst"|BuildingSecond->"BuildingSecond"|Comparing->"Comparing"|Revalidating->"Revalidating"|Qualifying->"Qualifying"|Cleanup->"Cleanup"|Complete->"Complete"|Refused->"Refused"|Indeterminate->"Unknown"
let build=function NoResult->"None"|Unknown->"Unknown"|Digest value->value
let owned state=if state.Owned.ContainsKey "a"&&state.Owned.ContainsKey "b" then "AB" elif state.Owned.ContainsKey "a" then "A" elif state.Owned.ContainsKey "b" then "B" else "None"
let running state=if state.Running.ContainsKey "a" then "A" elif state.Running.ContainsKey "b" then "B" else "None"
let pending=function None->"None"|Some(AcquireInputs _)->"Acquire"|Some(ValidateInputs _)->"Validate"|Some(CreateStore "a")->"CreateA"|Some(CreateStore _)->"CreateB"|Some(StartBuild("a",_))->"StartA"|Some(StartBuild _)->"StartB"|Some(AwaitBuild("a",_,_))->"AwaitA"|Some(AwaitBuild _)->"AwaitB"|Some(CancelBuild _)->"Cancel"|Some(CompareBuilds _)->"Compare"|Some(QualifyInactive _)->"Qualify"|Some(RemoveStore _)->"Remove"
let project state=
 let refusal=defaultArg state.Refusal "None"
 let qualification=match state.Qualification with QualificationUnknown->"Unknown"|Accepted->"Accepted"
 let fields=["phase",text(phase state.Phase);"current",boolean state.Current;"owned",text(owned state);"running",text(running state);"unknownBuild",boolean(not state.UnknownBuilds.IsEmpty);"first",text(build state.First);"second",text(build state.Second);"qualification",text qualification;"cancelled",boolean state.Cancelled;"cleanupFailed",boolean state.CleanupFailed;"refusal",text refusal;"pending",text(pending state.Pending);"lastEffect",text state.LastEffect]
 let draft:QuintReplayState={Identity=String.replicate 64 "0";Bindings=["state",QuintReplayValue.Record fields]}
 {draft with Identity=QuintReplay.stateFingerprint draft|>replay}
let req state=Runner.nextEffect state|>fst
let obs value state=Runner.observe value state
let cancel state=Runner.cancel state
let common=["requestAcquire",req;"observeAcquire",obs(InputsAcquired "input");"requestValidate",req;"observeValid",obs(InputsValidated "input");"requestStoreA",req;"observeStoreA",obs(StoreCreated("a","store-a"));"requestStoreB",req;"observeStoreB",obs(StoreCreated("b","store-b"))]
let started=common@["requestStartA",req;"observeStartedA",obs(BuildStarted("a","process-a"))]
let compared=started@["requestAwaitA",req;"observeBuildA",obs(BuildCompleted("a","process-a","aaa","input"));"requestStartB",req;"observeStartedB",obs(BuildStarted("b","process-b"));"requestAwaitB",req;"observeBuildB",obs(BuildCompleted("b","process-b","aaa","input"));"requestCompare",req;"observeMatch",obs(BuildsCompared true)]
let remove=["requestRemove",req;"observeRemoved",obs(StoreAbsent "store-a");"requestRemove",req;"observeRemoved",obs(StoreAbsent "store-b")]
let cases=[
 "success",compared@["requestValidate",req;"observeValid",obs(InputsValidated "input");"requestQualify",req;"observeQualified",obs(QualificationObserved(true,"input"))]@remove
 "lost",common@["requestStartA",req;"loseBuildA",obs(BuildMayHaveEffect "a")]@remove
 "mismatch",started@["requestAwaitA",req;"observeBuildA",obs(BuildCompleted("a","process-a","aaa","input"));"requestStartB",req;"observeStartedB",obs(BuildStarted("b","process-b"));"requestAwaitB",req;"observeBuildMismatch",obs(BuildCompleted("b","process-b","bbb","input"));"requestCompare",req;"observeMismatch",obs(BuildsCompared false)]@remove
 "cancellation",started@["cancel",cancel;"requestCancel",req;"observeCancelled",obs(BuildCancelled "process-a")]@remove
 "cleanupFailureCase",started@["cancel",cancel;"requestCancel",req;"observeCancelled",obs(BuildCancelled "process-a");"requestRemove",req;"cleanupFailure",obs(EffectFailed "remove-failed")]
 "stale",compared@["requestValidate",req;"observeStale",obs(InputsValidated "changed-input")]@remove]
let environment steps={Seed="20261002";Bounds=["steps",int64 steps];ToolFingerprint=fingerprint "quint-0.32.0";ProfileFingerprint=fingerprint "persistent-v3-runner";ContractFingerprint=fingerprint "persistent-v3-runner-qnt";AdapterFingerprint=fingerprint "typed-directory-fixture";ImplementationFingerprint=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof<State>.Assembly.Location))).ToLowerInvariant()}
let mutable transitions=0
for scenario,steps in cases do
 let states=steps|>List.scan(fun state (_,transition)->transition state)(Runner.initial "input")|>List.tail|>List.map project
 let actions=steps|>List.map fst
 let observations=List.map3(fun index action actual->{Index=index;Action=action;Source={Path="PersistentV3Runner.qnt";Line=1;Column=index};Actual=actual})[1..actions.Length] actions states
 let context={Environment=environment actions.Length;Steps=observations|>List.map(fun item->{Index=item.Index;Action=item.Action;Source=item.Source})}
 let file=Directory.GetFiles(root,scenario+"-*.itf.json")|>Array.exactlyOne
 let document=JsonNode.Parse(File.ReadAllText file).AsObject()
 let metadata=document["#meta"].AsObject()
 metadata.Remove("description")|>ignore
 metadata.Remove("timestamp")|>ignore
 metadata["status"]<-JsonValue.Create "ok"
 let trace=QuintReplay.decodeItf context (document.ToJsonString())|>replay
 match QuintReplay.compare trace observations with Ok QuintReplayResult.Equivalent->()|value->fail $"{scenario} correspondence failed: %A{value}"
 transitions<-transitions+steps.Length
printfn "persistent v3 runner FsQuint correspondence passed: %d scenarios, %d ordered transitions" cases.Length transitions
