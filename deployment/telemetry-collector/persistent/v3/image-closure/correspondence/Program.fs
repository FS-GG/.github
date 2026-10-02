open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks
open FSGG.Telemetry.PersistentV3.ImageClosure
open FsQuint

type ThrowingStart(inner:IRunnerMechanism)=
 interface IRunnerMechanism with
  member _.Execute(effect,token)=match effect with StartBuild _->Task.FromException<Observation>(InvalidOperationException "start-failed")|_->inner.Execute(effect,token)

let fail message=raise(InvalidOperationException message)
let replay=function Ok value->value|Error error->fail $"FsQuint: %A{error}"
let root=Environment.GetEnvironmentVariable "PERSISTENT_V3_RUNNER_ITF_ROOT"
if String.IsNullOrWhiteSpace root||not(Directory.Exists root)then fail "PERSISTENT_V3_RUNNER_ITF_ROOT required"
let required name=Environment.GetEnvironmentVariable name|>Option.ofObj|>Option.filter File.Exists|>Option.defaultWith(fun()->fail(name+" required"))
let modelPath=required "PERSISTENT_V3_RUNNER_MODEL"
let toolPath=required "PERSISTENT_V3_RUNNER_TOOL"
let adapterPath=required "PERSISTENT_V3_RUNNER_ADAPTER"
let profilePath=required "PERSISTENT_V3_RUNNER_PROFILE"
let productionPath=required "PERSISTENT_V3_RUNNER_PRODUCTION_DLL"
let fileSha path=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes path)).ToLowerInvariant()
let loadedPath=typeof<State>.Assembly.Location
if fileSha loadedPath<>fileSha productionPath then fail $"loaded production DLL differs: loaded={fileSha loadedPath} expected={fileSha productionPath}"
let text value=QuintReplayValue.Text value
let boolean value=QuintReplayValue.Boolean value
let fingerprint (value:string)=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes value)).ToLowerInvariant()
let phase=function Prepared->"Prepared"|Acquired->"Acquired"|Validated->"Validated"|CreatingStores->"CreatingStores"|BuildingFirst->"BuildingFirst"|BuildingSecond->"BuildingSecond"|Comparing->"Comparing"|Revalidating->"Revalidating"|Qualifying->"Qualifying"|Cleanup->"Cleanup"|Complete->"Complete"|C4Ready->"C4Ready"|Refused->"Refused"|Indeterminate->"Unknown"
let build=function NoResult->"None"|Unknown->"Unknown"|Digest value->value
let owned state=if state.Owned.ContainsKey "a"&&state.Owned.ContainsKey "b" then "AB" elif state.Owned.ContainsKey "a" then "A" elif state.Owned.ContainsKey "b" then "B" else "None"
let running state=if state.Running.ContainsKey "a" then "A" elif state.Running.ContainsKey "b" then "B" else "None"
let identityOk state=
 let stores=state.Owned|>Map.toSeq|>Seq.map snd|>Seq.toArray
 let processes=state.Running|>Map.toSeq|>Seq.map snd|>Seq.toArray
 state.IdentityOk&&Array.forall(fun value->not(String.IsNullOrWhiteSpace value)) stores&&Array.forall(fun value->not(String.IsNullOrWhiteSpace value)) processes&&(Array.distinct stores).Length=stores.Length&&(Array.distinct processes).Length=processes.Length&&(Set.intersect(Set.ofArray stores)(Set.ofArray processes)|>Set.isEmpty)
let pending=function None->"None"|Some(AcquireInputs _)->"Acquire"|Some(ValidateInputs _)->"Validate"|Some(CreateStore "a")->"CreateA"|Some(CreateStore _)->"CreateB"|Some(StartBuild("a",_))->"StartA"|Some(StartBuild _)->"StartB"|Some(AwaitBuild("a",_,_))->"AwaitA"|Some(AwaitBuild _)->"AwaitB"|Some(CancelBuild _)->"Cancel"|Some(CompareBuilds _)->"Compare"|Some(QualifyInactive _)->"Qualify"|Some(RemoveStore _)->"Remove"
let project state=
 let refusal=defaultArg state.Refusal "None"
 let qualification=match state.Qualification with QualificationUnknown->"Unknown"|Accepted->"Accepted"
 let fields=["target",text(if state.Target=C4Only then "C4Only" else "Full");"phase",text(phase state.Phase);"inputId",text state.ExpectedInput;"current",boolean state.Current;"owned",text(owned state);"running",text(running state);"storeUnknown",boolean(not state.MayHaveEffect.IsEmpty);"unknownBuild",boolean(not state.UnknownBuilds.IsEmpty);"identityOk",boolean(identityOk state);"first",text(build state.First);"second",text(build state.Second);"qualification",text qualification;"cancelled",boolean state.Cancelled;"cleanupFailed",boolean state.CleanupFailed;"refusal",text refusal;"pending",text(pending state.Pending);"lastEffect",text state.LastEffect]
 let draft:QuintReplayState={Identity=String.replicate 64 "0";Bindings=["state",QuintReplayValue.Record fields]}
 {draft with Identity=QuintReplay.stateFingerprint draft|>replay}
let req state=Runner.nextEffect state|>fst
let obs value state=Runner.observe value state
let cancel state=Runner.cancel state
let common=["requestAcquire",req;"observeAcquire",obs(InputsAcquired "input");"requestValidate",req;"observeValid",obs(InputsValidated "input");"requestStoreA",req;"observeStoreA",obs(StoreCreated("a","store-a"));"requestStoreB",req;"observeStoreB",obs(StoreCreated("b","store-b"))]
let started=common@["requestStartA",req;"observeStartedA",obs(BuildStarted("a","process-a"))]
let compared=started@["requestAwaitA",req;"observeBuildA",obs(BuildCompleted("a","process-a","aaa","input"));"requestStartB",req;"observeStartedB",obs(BuildStarted("b","process-b"));"requestAwaitB",req;"observeBuildB",obs(BuildCompleted("b","process-b","aaa","input"));"requestCompare",req;"observeMatch",obs(BuildsCompared true)]
let remove=["requestRemove",req;"observeRemoved",obs(StoreAbsent "store-a");"requestRemove",req;"observeRemoved",obs(StoreAbsent "store-b")]
let qualified=compared@["requestValidate",req;"observeValid",obs(InputsValidated "input");"requestQualify",req;"observeQualified",obs(QualificationObserved(true,"input"))]
let beforeFinalRemove=qualified@["requestRemove",req;"observeRemoved",obs(StoreAbsent "store-a");"requestRemove",req]
let deadlineAfterRemove state=Runner.observe(StoreAbsent "store-b")state|>Runner.invalidateBoundary "Deadline" false
let cancellationAfterRemove state=Runner.observe(StoreAbsent "store-b")state|>Runner.invalidateBoundary "Cancelled" true
let cases=[
 "success",qualified@remove
 "lost",common@["requestStartA",req;"loseBuildA",obs(BuildMayHaveEffect "a")]@remove
 "mismatch",started@["requestAwaitA",req;"observeBuildA",obs(BuildCompleted("a","process-a","aaa","input"));"requestStartB",req;"observeStartedB",obs(BuildStarted("b","process-b"));"requestAwaitB",req;"observeBuildMismatch",obs(BuildCompleted("b","process-b","bbb","input"));"requestCompare",req;"observeMismatch",obs(BuildsCompared false)]@remove
 "cancellation",started@["cancel",cancel;"requestCancel",req;"observeCancelled",obs(BuildCancelled "process-a")]@remove
 "cleanupFailureCase",started@["cancel",cancel;"requestCancel",req;"observeCancelled",obs(BuildCancelled "process-a");"requestRemove",req;"cleanupFailure",obs(EffectFailed "remove-failed")]
 "stale",compared@["requestValidate",req;"observeStale",obs(InputsValidated "changed-input")]@remove
 "duplicateStore",(common|>List.take 6)@["requestStoreB",req;"observeDuplicateStore",obs(StoreCreated("b","store-a"));"requestRemove",req;"observeRemoved",obs(StoreAbsent "store-a")]
 "duplicateProcess",common@["requestStartA",req;"observeDuplicateProcess",obs(BuildStarted("a","store-a"))]@remove
 "lateDeadline",beforeFinalRemove@["deadlineAfterRemove",deadlineAfterRemove]
 "lateCancellation",beforeFinalRemove@["cancellationAfterRemove",cancellationAfterRemove]
 "c4Success",compared@["requestValidate",req;"observeValid",obs(InputsValidated "input")]@remove
 "c4Stale",compared@["requestValidate",req;"observeStale",obs(InputsValidated "changed-input")]@remove
 "c4LateDeadline",compared@["requestValidate",req;"observeValid",obs(InputsValidated "input")]@(remove|>List.take 3)@["deadlineAfterRemove",deadlineAfterRemove]
 "c4LateCancellation",compared@["requestValidate",req;"observeValid",obs(InputsValidated "input")]@(remove|>List.take 3)@["cancellationAfterRemove",cancellationAfterRemove]
 "c4CleanupFailure",compared@["requestValidate",req;"observeValid",obs(InputsValidated "input");"requestRemove",req;"cleanupFailure",obs(EffectFailed "remove-failed")]
 "c4SealFailure",(compared|>List.take(compared.Length-1))@["comparisonSealFailure",obs(BuildsCompared false)]@remove]
let environment steps={Seed="20261002";Bounds=["steps",int64 steps];ToolFingerprint=fileSha toolPath;ProfileFingerprint=fileSha profilePath;ContractFingerprint=fileSha modelPath;AdapterFingerprint=fileSha adapterPath;ImplementationFingerprint=fileSha productionPath}
let mutable transitions=0
for scenario,steps in cases do
 let states=steps|>List.scan(fun state (_,transition)->transition state)(if scenario.StartsWith("c4",StringComparison.Ordinal) then Runner.initialC4 "input" else Runner.initial "input")|>List.tail|>List.map project
 let actions=steps|>List.map fst
 let observations=List.map3(fun index action actual->{Index=index;Action=action;Source={Path="PersistentV3Runner.qnt";Line=1;Column=index};Actual=actual})[1..actions.Length] actions states
 let context={Environment=environment actions.Length;Steps=observations|>List.map(fun item->{Index=item.Index;Action=item.Action;Source=item.Source})}
 let file=Directory.GetFiles(root,scenario+"-*.itf.json")|>Array.exactlyOne
 let document=JsonNode.Parse(File.ReadAllText file).AsObject()
 let metadata=document["#meta"].AsObject()
 if metadata["status"].GetValue<string>()<>"passed" then fail $"{scenario} original ITF status was not passed"
 metadata.Remove("description")|>ignore
 metadata.Remove("timestamp")|>ignore
 metadata["status"]<-JsonValue.Create "ok"
 let trace=QuintReplay.decodeItf context (document.ToJsonString())|>replay
 match QuintReplay.compare trace observations with Ok QuintReplayResult.Equivalent->()|value->fail $"{scenario} correspondence failed: %A{value}"
 transitions<-transitions+steps.Length
let actualRoot=Directory.CreateTempSubdirectory("p2c3-correspondence-").FullName
let actualMechanism=ThrowingStart(RunnerExecution.DirectoryFixture(actualRoot,"input","aaa","aaa") :> IRunnerMechanism)
let actualFinal,actualTrace=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 5.0) CancellationToken.None actualMechanism (Runner.initial "input")
if actualFinal.Phase<>Indeterminate||not actualFinal.Owned.IsEmpty then fail "actual bounded exception trace terminal differs"
let requestName=function AcquireInputs _->"requestAcquire"|ValidateInputs _->"requestValidate"|CreateStore "a"->"requestStoreA"|CreateStore _->"requestStoreB"|StartBuild("a",_)->"requestStartA"|StartBuild _->"requestStartB"|AwaitBuild("a",_,_)->"requestAwaitA"|AwaitBuild _->"requestAwaitB"|CancelBuild _->"requestCancel"|CompareBuilds _->"requestCompare"|QualifyInactive _->"requestQualify"|RemoveStore _->"requestRemove"
let observationName=function InputsAcquired _->"observeAcquire"|InputsValidated _->"observeValid"|StoreCreated("a",_)->"observeStoreA"|StoreCreated _->"observeStoreB"|StoreMayHaveEffect _->"observeDuplicateStore"|BuildStarted("a",_)->"observeStartedA"|BuildStarted _->"observeStartedB"|BuildMayHaveEffect _->"loseBuildA"|BuildCompleted("a",_,_,_)->"observeBuildA"|BuildCompleted _->"observeBuildB"|BuildCancelled _->"observeCancelled"|BuildsCompared true->"observeMatch"|BuildsCompared false->"observeMismatch"|QualificationObserved _->"observeQualified"|StoreAbsent _->"observeRemoved"|EffectFailed _->"cleanupFailure"
let mutable actualState=Runner.initial "input"
let actualSteps=ResizeArray<string*QuintReplayState>()
for effect,observation in actualTrace do
 let requested,_=Runner.nextEffect actualState
 actualSteps.Add(requestName effect,project requested)
 actualState<-Runner.observe observation requested
 actualSteps.Add(observationName observation,project actualState)
let actions=actualSteps|>Seq.map fst|>Seq.toList
let observations=actualSteps|>Seq.mapi(fun offset (action,state)->{Index=offset+1;Action=action;Source={Path="PersistentV3Runner.qnt";Line=1;Column=offset+1};Actual=state})|>Seq.toList
let context={Environment=environment actions.Length;Steps=observations|>List.map(fun item->{Index=item.Index;Action=item.Action;Source=item.Source})}
let lostFile=Directory.GetFiles(root,"lost-*.itf.json")|>Array.exactlyOne
let lostDocument=JsonNode.Parse(File.ReadAllText lostFile).AsObject()
let lostMetadata=lostDocument["#meta"].AsObject()
if lostMetadata["status"].GetValue<string>()<>"passed" then fail "lost original ITF status was not passed"
lostMetadata.Remove("description")|>ignore;lostMetadata.Remove("timestamp")|>ignore;lostMetadata["status"]<-JsonValue.Create "ok"
let actualCanonical=QuintReplay.decodeItf context (lostDocument.ToJsonString())|>replay
match QuintReplay.compare actualCanonical observations with Ok QuintReplayResult.Equivalent->()|value->fail $"actual bounded exception trace correspondence failed: %A{value}"
Directory.Delete(actualRoot,true)
printfn "persistent v3 runner FsQuint correspondence passed: %d scenarios, %d ordered transitions; actual exception trace and loaded DLL joined" cases.Length transitions
