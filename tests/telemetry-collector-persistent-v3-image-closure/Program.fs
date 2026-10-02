open System
open System.IO
open System.Security.Cryptography
open System.Text.Json
open FSGG.Telemetry.PersistentV3.ImageClosure

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
let selected={IdentityClass="synthetic-test";AcquisitionRoot=root;OwnerUid=ImageClosure.ownerUid root;Platform="linux/amd64";HostSourceRevision=h 'a';HostReleaseId=1L;HostRunId=2L;HostArchiveSha256=h 'b';HostPackageSha256=h 'c';HostPayloadSha256=h 'd';HostManifestSha256=h 'e';HostJournalSha256=h 'f';ManagerSourceRevision=String('a',40);ManagerSourceTree=String('b',40);ManagerArtifactSha256=h '1';ManagerManifestSha256=h '2';ManagerPreparedSha256=h '3';ManagerArchiveSha256=h '4';ManagerRunId=3L;ManagerArtifactId=4L;RuntimeImageDigest="sha256:"+h '5';RuntimeTreeSha256=h '6';NativeElfSha256=h '7';NativeProfileSha256=h '8';ReaderProfileSha256=h '9';CanonicalVerifierSha256=h 'a';InventorySha256=inventory rows;Files=rows}
let prepared:byte array=match ImageClosure.prepare selected with ClosureResult.Prepared bytes->bytes|value->fail $"physical positive refused: %A{value}"
let context=JsonDocument.Parse prepared
if context.RootElement.GetProperty("schema").GetString()<>"fsgg.telemetry.persistent-v3-image-context/2"||not(context.RootElement.GetProperty("containerfile").GetString().Contains("ENTRYPOINT []"))||not(context.RootElement.GetProperty("profile").GetString().Contains("\"user\":32768")) then fail "concrete context/profile absent"
let refuses name expected mutate=match ImageClosure.prepare(mutate selected)with ClosureResult.Refused actual when actual=expected->()|value->fail $"{name} did not refuse at {expected}: %A{value}"
let withRows next value={value with Files=next;InventorySha256=inventory next}
refuses "nonexistent" "acquisition-root-inventory-incomplete" (withRows [|{rows[0] with SourcePath="host/nonexistent.dll"};rows[1];rows[2];rows[3]|])
refuses "path" "acquisition-root-inventory-incomplete" (withRows [|{rows[0] with SourcePath="../escape"};rows[1];rows[2];rows[3]|])
refuses "mode" "inventory-physical-mismatch" (withRows [|{rows[0] with Mode="0555"};rows[1];rows[2];rows[3]|])
refuses "bytes" "inventory-physical-mismatch" (withRows [|{rows[0] with Bytes=rows[0].Bytes+1L};rows[1];rows[2];rows[3]|])
refuses "hash" "inventory-physical-mismatch" (withRows [|{rows[0] with Sha256=h 'd'};rows[1];rows[2];rows[3]|])
refuses "omission" "inventory-omits-dependency-root" (withRows [|rows[0];rows[1];rows[2]|])
refuses "duplicate" "inventory-order-or-duplicate" (withRows [|rows[0];rows[0];rows[2];rows[3]|])
refuses "platform" "selection-shape" (fun value->{value with Platform="linux/arm64"})
refuses "base identity" "fixed-production-identity-mismatch" (fun value->{value with IdentityClass="production";OwnerUid=0;RuntimeImageDigest="sha256:"+h '0'})
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead|||UnixFileMode.UserWrite)
File.WriteAllText(Path.Combine(root,rows[0].SourcePath),"mutated-after-validation")
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead)
refuses "input mutation" "inventory-physical-mismatch" id
File.Delete(Path.Combine(root,rows[0].SourcePath))
File.CreateSymbolicLink(Path.Combine(root,rows[0].SourcePath),"../runtime/verify.py")|>ignore
refuses "symbolic link" "inventory-regular-file-or-custody" (withRows [|{rows[0] with Bytes=rows[3].Bytes;Sha256=rows[3].Sha256};rows[1];rows[2];rows[3]|])

let runRoot=Directory.CreateTempSubdirectory("p2c3-runner-").FullName
let final,trace=RunnerExecution.run 32 (RunnerExecution.DirectoryFixture(runRoot,"input-1","image-x","image-x")) (Runner.initial "input-1")
if not(Runner.qualificationAccepted final)||trace.Length<>13||trace|>List.map(fst>>function CreateStore _->"create"|StartBuild _->"start"|AwaitBuild _->"await"|CancelBuild _->"cancel"|ValidateInputs _->"validate"|AcquireInputs _->"acquire"|CompareBuilds _->"compare"|QualifyInactive _->"qualify"|RemoveStore _->"remove")<>["acquire";"validate";"create";"create";"start";"await";"start";"await";"compare";"validate";"qualify";"remove";"remove"] then fail "typed runner command order/terminal differs"
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
printfn "persistent v3 image closure: physical context plus 11 predicate-specific refusals passed; typed runner executed %d ordered effects" trace.Length
Directory.Delete(root,true)
Directory.Delete(runRoot,true)
