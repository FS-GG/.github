open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSGG.Telemetry.PersistentV3.ImageClosure

type DelegateMechanism(handler:Effect*CancellationToken->Task<Observation>)=
  interface IRunnerMechanism with member _.Execute(effect,token)=handler(effect,token)

let fail message=raise(InvalidOperationException message)
let sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let write (root:string) (relative:string) (mode:UnixFileMode) (text:string) =
  let path=Path.Combine(root,relative)
  Directory.CreateDirectory(Path.GetDirectoryName path)|>ignore
  File.WriteAllText(path,text)
  File.SetUnixFileMode(path,mode)
  path
let root=Directory.CreateTempSubdirectory("p2c3-physical-").FullName
File.SetUnixFileMode(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let readMode=UnixFileMode.UserRead|||UnixFileMode.GroupRead|||UnixFileMode.OtherRead
let executeMode=readMode|||UnixFileMode.UserExecute|||UnixFileMode.GroupExecute|||UnixFileMode.OtherExecute
let definitions=["host/host.dll","host",readMode,"host-actual";"manager/manager.dll","manager",readMode,"manager-actual";"native/codex","native",executeMode,"native-actual";"runtime/verify.py","runtime",readMode,"verifier-actual"]
let rows=
  definitions
  |>List.map(fun(relative,sourceClass,mode,text)->
      let path=write root relative mode text
      {SourcePath=relative;TargetPath="/opt/fsgg/image/"+relative;Bytes=FileInfo(path).Length;Sha256=sha(File.ReadAllBytes path);Mode=(if mode.HasFlag UnixFileMode.UserExecute then "0555" else "0444");SourceClass=sourceClass})
  |>List.toArray
let inventory value=sha(JsonSerializer.SerializeToUtf8Bytes value)
let h c=String(c,64)
let manifestRoot=Directory.CreateTempSubdirectory("p2c3-manifests-").FullName
File.SetUnixFileMode(manifestRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let authority role=match role with "host"->h 'e'|"manager"->h '2'|"runtime"->h '6'|_->h '8'
let roleManifest role =
  let files=rows|>Array.filter(fun row->row.SourceClass=role)|>Array.map(fun row->{SourcePath=row.SourcePath;TargetPath=row.TargetPath;Bytes=row.Bytes;Sha256=row.Sha256;Mode=row.Mode})
  {Schema="fsgg.telemetry.persistent-v3-role-closure/1";Role=role;AuthoritySha256=authority role;SearchRoots=[|"/opt/fsgg/image/"+role|];Argv=(if role="native" then [|"/opt/fsgg/image/native/codex"|] else [||]);LoaderPaths=(if role="native" then [|"/opt/fsgg/image/runtime/verify.py"|] else [||]);Files=files}
let writeManifest name (manifest:RoleManifest) : RoleManifestReference =
  let bytes=JsonSerializer.SerializeToUtf8Bytes manifest
  let path=Path.Combine(manifestRoot,name+".json")
  File.WriteAllBytes(path,bytes)
  File.SetUnixFileMode(path,readMode)
  {Role=manifest.Role;Path=name+".json";Sha256=sha bytes}
let manifests=[|"host";"manager";"native";"runtime"|]|>Array.map(fun role->writeManifest role (roleManifest role))
let selected={IdentityClass="synthetic-test";AcquisitionRoot=root;ManifestRoot=manifestRoot;RoleManifests=manifests;OwnerUid=ImageClosure.ownerUid root;Platform="linux/amd64";HostSourceRevision=h 'a';HostReleaseId=1L;HostRunId=2L;HostArchiveSha256=h 'b';HostPackageSha256=h 'c';HostPayloadSha256=h 'd';HostManifestSha256=h 'e';HostJournalSha256=h 'f';ManagerSourceRevision=String('a',40);ManagerSourceTree=String('b',40);ManagerArtifactSha256=h '1';ManagerManifestSha256=h '2';ManagerPreparedSha256=h '3';ManagerArchiveSha256=h '4';ManagerRunId=3L;ManagerArtifactId=4L;RuntimeImageDigest="sha256:"+h '5';RuntimeTreeSha256=h '6';NativeElfSha256=h '7';NativeProfileSha256=h '8';ReaderProfileSha256=h '9';CanonicalVerifierSha256=h 'a';InventorySha256=inventory rows;Files=rows}
let prepared:byte array=match ImageClosure.prepare selected with ClosureResult.Prepared bytes->bytes|value->fail $"physical positive refused: %A{value}"
let production={selected with IdentityClass="production";OwnerUid=0;HostSourceRevision=ImageClosure.HostSourceRevision;HostReleaseId=401538149L;HostRunId=36964135216L;HostArchiveSha256=ImageClosure.HostArchiveSha256;HostPackageSha256=ImageClosure.HostPackageSha256;HostPayloadSha256=ImageClosure.HostPayloadSha256;HostManifestSha256=ImageClosure.HostManifestSha256;HostJournalSha256=ImageClosure.HostJournalSha256;ManagerSourceRevision=ImageClosure.ManagerSourceRevision;ManagerSourceTree=ImageClosure.ManagerSourceTree;ManagerArtifactSha256=ImageClosure.ManagerArtifactSha256;ManagerManifestSha256=ImageClosure.ManagerManifestSha256;ManagerPreparedSha256=ImageClosure.ManagerPreparedSha256;ManagerArchiveSha256=ImageClosure.ManagerArchiveSha256;ManagerRunId=36983338783L;ManagerArtifactId=11216418410L;RuntimeImageDigest=ImageClosure.RuntimeImageDigest;RuntimeTreeSha256=ImageClosure.RuntimeTreeSha256;NativeElfSha256=ImageClosure.NativeElfSha256;NativeProfileSha256=ImageClosure.NativeProfileSha256;CanonicalVerifierSha256=ImageClosure.CanonicalVerifierSha256}
match ImageClosure.prepare production with ClosureResult.Unavailable "trusted-production-role-manifests-acquisition-required"->()|value->fail $"unacquired production manifests emitted context: %A{value}"
let context=JsonDocument.Parse prepared
let containerfile=context.RootElement.GetProperty("containerfile").GetString()
if context.RootElement.GetProperty("schema").GetString()<>"fsgg.telemetry.persistent-v3-image-context/2"||not(containerfile.StartsWith("FROM mcr.microsoft.com/dotnet/aspnet@sha256:"))||not(containerfile.Contains("COPY --chown=32768:32768 --chmod=0444 [\"host/host.dll\",\"/opt/fsgg/image/host/host.dll\"]"))||not(containerfile.Contains("ENTRYPOINT []"))||not(context.RootElement.GetProperty("profile").GetString().Contains("\"group\":32768")) then fail "concrete context/profile absent"
let refuses name expected mutate=match ImageClosure.prepare(mutate selected)with ClosureResult.Refused actual when actual=expected->()|value->fail $"{name} did not refuse at {expected}: %A{value}"
let withRows next value={value with Files=next;InventorySha256=inventory next}
refuses "nonexistent" "acquisition-root-inventory-incomplete" (withRows [|{rows[0] with SourcePath="host/nonexistent.dll"};rows[1];rows[2];rows[3]|])
refuses "path" "inventory-entry" (withRows [|{rows[0] with SourcePath="../escape"};rows[1];rows[2];rows[3]|])
refuses "mode" "inventory-physical-mismatch" (withRows [|{rows[0] with Mode="0555"};rows[1];rows[2];rows[3]|])
refuses "bytes" "inventory-physical-mismatch" (withRows [|{rows[0] with Bytes=rows[0].Bytes+1L};rows[1];rows[2];rows[3]|])
refuses "hash" "inventory-physical-mismatch" (withRows [|{rows[0] with Sha256=h 'd'};rows[1];rows[2];rows[3]|])
refuses "omission" "inventory-omits-dependency-root" (withRows [|rows[0];rows[1];rows[2]|])
refuses "duplicate" "inventory-order-or-duplicate" (withRows [|rows[0];rows[0];rows[2];rows[3]|])
refuses "duplicate source alias" "inventory-order-or-duplicate" (withRows [|rows[0];{rows[1] with SourcePath=rows[0].SourcePath};rows[2];rows[3]|])
refuses "source whitespace" "inventory-entry" (withRows [|{rows[0] with SourcePath="host/host file.dll"};rows[1];rows[2];rows[3]|])
refuses "target newline" "inventory-entry" (withRows [|{rows[0] with TargetPath="/opt/fsgg/image/host/host.dll\nRUN false"};rows[1];rows[2];rows[3]|])
refuses "platform" "selection-shape" (fun value->{value with Platform="linux/arm64"})
refuses "base identity" "fixed-production-identity-mismatch" (fun value->{value with IdentityClass="production";OwnerUid=0;RuntimeImageDigest="sha256:"+h '0'})
refuses "role manifest omission" "role-manifest-set" (fun value->{value with RoleManifests=manifests[0..2]})
refuses "role manifest digest" "role-manifest-digest" (fun value->{value with RoleManifests=manifests|>Array.map(fun item->if item.Role="host" then {item with Sha256=h 'd'} else item)})
let badNative=writeManifest "native-bad" {roleManifest "native" with Argv=[||]}
refuses "native argv layout" "native-layout" (fun value->{value with RoleManifests=manifests|>Array.map(fun item->if item.Role="native" then badNative else item)})
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead|||UnixFileMode.UserWrite)
File.WriteAllText(Path.Combine(root,rows[0].SourcePath),"mutated-after-validation")
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead)
refuses "input mutation" "inventory-physical-mismatch" id
File.Delete(Path.Combine(root,rows[0].SourcePath))
File.CreateSymbolicLink(Path.Combine(root,rows[0].SourcePath),"../runtime/verify.py")|>ignore
refuses "symbolic link" "acquisition-census-link" (withRows [|{rows[0] with Bytes=rows[3].Bytes;Sha256=rows[3].Sha256};rows[1];rows[2];rows[3]|])

let runRoot=Directory.CreateTempSubdirectory("p2c3-runner-").FullName
let final,trace=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 10.0) CancellationToken.None (RunnerExecution.DirectoryFixture(runRoot,"input-1","image-x","image-x")) (Runner.initial "input-1")
if not(Runner.qualificationAccepted final)||trace.Length<>13||trace|>List.map(fst>>function CreateStore _->"create"|StartBuild _->"start"|AwaitBuild _->"await"|CancelBuild _->"cancel"|ValidateInputs _->"validate"|AcquireInputs _->"acquire"|CompareBuilds _->"compare"|QualifyInactive _->"qualify"|RemoveStore _->"remove")<>["acquire";"validate";"create";"create";"start";"await";"start";"await";"compare";"validate";"qualify";"remove";"remove"] then fail "typed runner command order/terminal differs"
let exceptionRoot=Directory.CreateTempSubdirectory("p2c3-exception-").FullName
let baseException=RunnerExecution.DirectoryFixture(exceptionRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let startThrows=DelegateMechanism(fun(effect,token)->match effect with StartBuild _->Task.FromException<Observation>(InvalidOperationException "start-failed")|_->baseException.Execute(effect,token))
let exceptionFinal,exceptionTrace=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 5.0) CancellationToken.None startThrows (Runner.initial "input-1")
if exceptionFinal.Phase<>Indeterminate||not exceptionFinal.Owned.IsEmpty||exceptionFinal.UnknownBuilds<>set["a"]||(Directory.EnumerateFileSystemEntries(exceptionRoot)|>Seq.isEmpty|>not)||not(exceptionTrace|>List.exists(fun(_,observation)->observation=BuildMayHaveEffect "a")) then fail "mechanism exception did not preserve uncertainty and clean known stores"
let blockedRoot=Directory.CreateTempSubdirectory("p2c3-blocked-").FullName
let baseBlocked=RunnerExecution.DirectoryFixture(blockedRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let never=TaskCompletionSource<Observation>()
let blocked=DelegateMechanism(fun(effect,token)->match effect with StartBuild _->never.Task|_->baseBlocked.Execute(effect,token))
let blockedFinal,_=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 3.0) CancellationToken.None blocked (Runner.initial "input-1")
if blockedFinal.Phase<>Indeterminate||not blockedFinal.Owned.IsEmpty||(Directory.EnumerateFileSystemEntries(blockedRoot)|>Seq.isEmpty|>not) then fail "bounded blocked effect did not clean known stores"
let duplicateA={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a","same-id"];Pending=Some(CreateStore "b")}
let duplicate=Runner.observe(StoreCreated("b","same-id"))duplicateA
if duplicate.Phase<>Cleanup||duplicate.MayHaveEffect<>set["b"] then fail "duplicate store identity was admitted"
let duplicateProcess={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a","store-a";"b","store-b"];Pending=Some(StartBuild("a","input-1"))}
let processAlias=Runner.observe(BuildStarted("a","store-a"))duplicateProcess
if processAlias.Phase<>Cleanup||processAlias.UnknownBuilds<>set["a"] then fail "store/process identity alias was admitted"
let cleanupRoot=Directory.CreateTempSubdirectory("p2c3-cleanup-").FullName
let ownedPath=Path.Combine(cleanupRoot,"owned-a")
Directory.CreateDirectory ownedPath|>ignore
let cleanupState={Runner.initial "input-1" with Phase=Cleanup;Owned=Map.ofList["a",ownedPath];Cancelled=true}
let wrongAbsent=DelegateMechanism(fun(effect,_)->match effect with RemoveStore _->Task.FromResult(StoreAbsent "wrong-id")|_->Task.FromResult(EffectFailed "unexpected"))
let wrongFinal,_=RunnerExecution.run 3 (DateTimeOffset.UtcNow.AddSeconds 2.0) CancellationToken.None wrongAbsent cleanupState
if wrongFinal.Owned.IsEmpty||wrongFinal.Phase<>Indeterminate then fail "wrong absence identity removed ownership or hid bounded exhaustion"
let cleanupThrows=DelegateMechanism(fun(effect,_)->match effect with RemoveStore _->Task.FromException<Observation>(IOException "cleanup-failed")|_->Task.FromResult(EffectFailed "unexpected"))
let cleanupFinal,_=RunnerExecution.run 3 (DateTimeOffset.UtcNow.AddSeconds 2.0) CancellationToken.None cleanupThrows cleanupState
if not cleanupFinal.CleanupFailed||cleanupFinal.Owned.IsEmpty then fail "cleanup exception was not sticky"
let cancelRoot=Directory.CreateTempSubdirectory("p2c3-cancel-").FullName
let cancelA=Path.Combine(cancelRoot,"a")
let cancelB=Path.Combine(cancelRoot,"b")
Directory.CreateDirectory cancelA|>ignore;Directory.CreateDirectory cancelB|>ignore
let cancelState={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a",cancelA;"b",cancelB]}
let cancelledToken=new CancellationTokenSource()
cancelledToken.Cancel()
let cancelFinal,_=RunnerExecution.run 8 (DateTimeOffset.UtcNow.AddSeconds 2.0) cancelledToken.Token (RunnerExecution.DirectoryFixture(cancelRoot,"input-1","x","x")) cancelState
if cancelFinal.Phase<>Refused||not cancelFinal.Owned.IsEmpty||(Directory.EnumerateFileSystemEntries(cancelRoot)|>Seq.isEmpty|>not) then fail "external cancellation did not clean owned stores"
let deadlineRoot=Directory.CreateTempSubdirectory("p2c3-deadline-").FullName
let deadlineStore=Path.Combine(deadlineRoot,"owned")
Directory.CreateDirectory deadlineStore|>ignore
let deadlineState={Runner.initial "input-1" with Phase=Cleanup;Owned=Map.ofList["a",deadlineStore]}
let deadlineFinal,_=RunnerExecution.run 8 (DateTimeOffset.UtcNow.AddMilliseconds -1.0) CancellationToken.None (RunnerExecution.DirectoryFixture(deadlineRoot,"input-1","x","x")) deadlineState
if deadlineFinal.Phase<>Indeterminate||deadlineFinal.Refusal<>Some "Deadline"||deadlineFinal.Owned.IsEmpty||not(Directory.Exists deadlineStore) then fail "absolute deadline did not stop effects with truthful retained ownership"
let acquired=Runner.initial "input-1"|>Runner.nextEffect|>fun(state,_)->Runner.observe(InputsAcquired "input-1")state|>Runner.nextEffect|>fun(state,_)->Runner.observe(InputsValidated "input-1")state
let storeA,_=Runner.nextEffect acquired
let withA=Runner.observe(StoreCreated("a","owned-a")) storeA
let storeB,_=Runner.nextEffect withA
let withStores=Runner.observe(StoreCreated("b","owned-b")) storeB
let requested,_=Runner.nextEffect withStores
let lost=Runner.observe(BuildMayHaveEffect "a") requested
if lost.Phase<>Cleanup||lost.UnknownBuilds<>set["a"]||lost.Qualification<>QualificationUnknown then fail "lost build acknowledgement was not unknown"
let cancelled=Runner.cancel {acquired with Phase=CreatingStores;Owned=Map.ofList["a","owned-a"]}
if cancelled.Phase<>Cleanup||not cancelled.Cancelled then fail "cancellation did not enter cleanup"
printfn "persistent v3 image closure: trusted role manifests plus 17 predicate-specific refusals passed; typed runner executed %d ordered effects" trace.Length
Directory.Delete(root,true)
Directory.Delete(manifestRoot,true)
Directory.Delete(runRoot,true)
Directory.Delete(exceptionRoot,true)
Directory.Delete(blockedRoot,true)
Directory.Delete(cleanupRoot,true)
Directory.Delete(cancelRoot,true)
Directory.Delete(deadlineRoot,true)
