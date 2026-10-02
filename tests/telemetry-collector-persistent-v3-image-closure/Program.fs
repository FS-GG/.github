open System
open System.Security.Cryptography
open System.Text.Json
open FSGG.Telemetry.PersistentV3.ImageClosure

let fail message=raise(InvalidOperationException message)
let sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let h c=String(c,64)
let rows=[|{Path="/opt/fsgg/image/host/host.dll";Bytes=1L;Sha256=h 'a';Mode="0444";SourceClass="host"};{Path="/opt/fsgg/image/manager/manager.dll";Bytes=2L;Sha256=h 'b';Mode="0444";SourceClass="manager"};{Path="/opt/fsgg/image/native/codex";Bytes=3L;Sha256=ImageClosure.NativeElfSha256;Mode="0555";SourceClass="native"};{Path="/opt/fsgg/image/runtime/verify.py";Bytes=4L;Sha256=ImageClosure.CanonicalVerifierSha256;Mode="0444";SourceClass="runtime"}|]
let inventory=JsonSerializer.SerializeToUtf8Bytes rows
let selected={Platform="linux/amd64";HostSourceRevision=ImageClosure.HostSourceRevision;HostReleaseId=401538149L;HostRunId=36964135216L;HostArchiveSha256=ImageClosure.HostArchiveSha256;HostPackageSha256=ImageClosure.HostPackageSha256;HostPayloadSha256=ImageClosure.HostPayloadSha256;HostManifestSha256=ImageClosure.HostManifestSha256;HostJournalSha256=ImageClosure.HostJournalSha256;ManagerSourceRevision=ImageClosure.ManagerSourceRevision;ManagerSourceTree=ImageClosure.ManagerSourceTree;ManagerArtifactSha256=ImageClosure.ManagerArtifactSha256;ManagerManifestSha256=ImageClosure.ManagerManifestSha256;ManagerPreparedSha256=ImageClosure.ManagerPreparedSha256;ManagerArchiveSha256=ImageClosure.ManagerArchiveSha256;ManagerRunId=36983338783L;ManagerArtifactId=11216418410L;RuntimeImageDigest=ImageClosure.RuntimeImageDigest;RuntimeTreeSha256=ImageClosure.RuntimeTreeSha256;NativeElfSha256=ImageClosure.NativeElfSha256;NativeProfileSha256=ImageClosure.NativeProfileSha256;ReaderProfileSha256=h 'c';CanonicalVerifierSha256=ImageClosure.CanonicalVerifierSha256;InventorySha256=sha inventory;Files=rows}
match ImageClosure.prepare selected with ClosureResult.Prepared bytes when bytes.Length>0 -> () | value -> fail $"positive closure failed: %A{value}"
let refuses name mutate = match ImageClosure.prepare(mutate selected) with ClosureResult.Refused _ -> () | value -> fail $"{name} did not refuse: %A{value}"
refuses "host" (fun value->{value with HostPackageSha256=h 'd'})
refuses "manager" (fun value->{value with ManagerArtifactSha256=h 'd'})
refuses "runtime" (fun value->{value with RuntimeImageDigest="sha256:"+h 'd'})
refuses "native" (fun value->{value with NativeElfSha256=h 'd'})
refuses "platform" (fun value->{value with Platform="linux/arm64"})
refuses "inventory digest" (fun value->{value with InventorySha256=h 'd'})
let withRows next value={value with Files=next;InventorySha256=sha(JsonSerializer.SerializeToUtf8Bytes next)}
refuses "path" (withRows [|{rows[0] with Path="/tmp/escape"};rows[1];rows[2];rows[3]|])
refuses "mode" (withRows [|{rows[0] with Mode="0777"};rows[1];rows[2];rows[3]|])
refuses "bytes" (withRows [|{rows[0] with Bytes=0L};rows[1];rows[2];rows[3]|])
refuses "omission" (withRows [|rows[0];rows[1];rows[2]|])
refuses "duplicate" (fun value->{value with Files=[|rows[0];rows[0]|]})
match ImageClosure.prepare {selected with InventorySha256="";Files=[||]} with Unavailable _ -> () | value -> fail $"missing closure was not unavailable: %A{value}"
let success=Runner.initial|>Runner.apply Acquire|>Runner.apply Validate|>Runner.apply CreateStores|>Runner.apply(FirstBuilt "same")|>Runner.apply(SecondBuilt "same")|>Runner.apply Qualify|>Runner.apply(RemoveStore "build-a")|>Runner.apply(RemoveStore "build-b")|>Runner.apply Finish
if not(Runner.qualificationAccepted success) then fail "runner success did not close"
let lost=Runner.initial|>Runner.apply Acquire|>Runner.apply Validate|>Runner.apply CreateStores|>Runner.apply FirstLost
if lost.First<>Unknown||lost.Qualification<>QualificationUnknown then fail "lost acknowledgement became success"
let cancelled=Runner.initial|>Runner.apply Acquire|>Runner.apply Validate|>Runner.apply CreateStores|>Runner.apply Cancel
if cancelled.Qualification<>QualificationUnknown||cancelled.Phase<>Cleanup then fail "cancellation did not preserve unknown"
let stale=Runner.initial|>Runner.apply Acquire|>Runner.apply Validate|>Runner.apply CreateStores|>Runner.apply(FirstBuilt "same")|>Runner.apply(SecondBuilt "same")|>Runner.apply Invalidate
let guarded=Runner.applyStaleResultMutationForCorrespondence false stale
let mutant=Runner.applyStaleResultMutationForCorrespondence true stale
if guarded.Qualification<>QualificationUnknown||mutant.Qualification<>Accepted then fail "stale-result guard mutant did not diverge"
printfn "persistent v3 image closure: exact selection and 12 refusal controls passed; runner lifecycle and stale-result guard mutant diverged"
