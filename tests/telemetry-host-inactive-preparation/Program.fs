open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Security.Cryptography
open FS.GG.Telemetry.Host

let check condition message=if not condition then failwith message
let json value=JsonSerializer.SerializeToUtf8Bytes value
let hash (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let fileHash path=hash(File.ReadAllBytes path)
let privateMode=UnixFileMode.UserRead|||UnixFileMode.UserWrite
let root=Directory.CreateTempSubdirectory("inactive-host-source-").FullName
File.SetUnixFileMode(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let put name mode bytes=
    let path=Path.Combine(root,name)
    File.WriteAllBytes(path,bytes);File.SetUnixFileMode(path,mode);path
let configPath=Path.Combine(root,"host.json")
let evidence=Path.Combine(root,"evidence")
let home=Path.Combine(root,"home")
for path in [evidence;home] do Directory.CreateDirectory(path,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
let sentinel=Path.Combine(root,"FORBIDDEN-PROCESS")
let executable=put "never-run" (privateMode|||UnixFileMode.UserExecute) (Encoding.UTF8.GetBytes("#!/bin/sh\ntouch "+sentinel+"\nexit 99\n"))
let modulePath=put "verifier.py" privateMode (File.ReadAllBytes(Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__,"../../tools/learn_01_native_source.py"))))
let emptyNames=[|"compression-init";"compression-common-init";"email-mime-init";"pydoc-data-init";"urllib-init"|]
let empties=emptyNames|>Array.map(fun name->put name (enum<UnixFileMode>0x124) [||])
let secret=put "inaccessible-secret" (enum<UnixFileMode>0) (Encoding.UTF8.GetBytes "not-opened-by-reader")
let profile=put "evidence/fixed-native-capability-profile.json" privateMode (json {|schema="frozen-test-profile/1"|})
let uid=File.ReadAllLines("/proc/self/status")|>Array.find(fun line->line.StartsWith("Uid:"))|>fun line->line.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)[1]|>Int64.Parse
let config=json {|Schema="fsgg.telemetry.host-config/2";Credentials=[|{|Reference="fixture";SecretFile=secret;WorkspaceId="fixture-workspace";ProducerId="fixture-producer";StreamId="fixture-stream";Role="native-collector";GrantId="fixture-grant";GrantGeneration=1L;Revoked=false|}|]|}
put "host.json" privateMode config|>ignore
let rows=Array.append [|executable;modulePath|] empties|>Array.sort|>Array.map(fun path->{|path=path;bytes=FileInfo(path).Length;sha256=fileHash path|})
let manifest=json {|schema="fsgg.telemetry.native-verifier-runtime/1";sourceRevision=String('a',40);runtimeImageDigest="sha256:"+String('b',64);runtimeExecutablePath=executable;modulePath=modulePath;files=rows|}
let manifestPath=put "manifest.json" privateMode manifest
let source=json {|schema="fsgg.telemetry.persistent-source-references/3";profileSha256=String('c',64);nativeSourceVolume="fixture-source";developmentTarget=home;collectorReadOnlyTarget=home;readerProfileSha256=fileHash profile;captureQualified=false;verifierRuntimeManifestSha256=hash manifest|}
put "source-reference.json" privateMode source|>ignore
let sidecar=json {|Schema="fsgg.telemetry.native-collector-installation/3";CredentialReference="fixture";ExecutablePath=executable;CodexHome=home;EvidenceRoot=evidence;Provider="openai";Model="gpt-5.6-sol";Effort="medium";ExecutableSha256=fileHash executable;NativeVerifier={|RuntimeExecutablePath=executable;RuntimeExecutableSha256=fileHash executable;ModulePath=modulePath;ModuleSha256=fileHash modulePath;RuntimeManifestPath=manifestPath;RuntimeManifestSha256=hash manifest|}|}
put "host.json.native-collector.json" privateMode sidecar|>ignore
let receipt=json {|schema="fsgg.telemetry.native-collector-installation-receipt/3";status="installed";ownerUid=uid;hostConfigSha256=hash config;sidecarSha256=hash sidecar;executableSha256=fileHash executable;credentialReference="fixture";workspaceId="fixture-workspace";producerId="fixture-producer";streamId="fixture-stream";grantId="fixture-grant";grantGeneration=1L;sourceVerification="unknown";snapshotOrigin="unknown";sharedCostCompleteness="unknown";activationAuthorized=false;sourceReferenceSha256=hash source;verifierRuntimeManifestSha256=hash manifest|}
let receiptPath=put "host.json.native-collector.receipt.json" privateMode receipt
let installChangedManifest (bytes:byte array)=
    let digest=hash bytes
    let changedSide=JsonNode.Parse(sidecar).AsObject()
    changedSide["NativeVerifier"]["RuntimeManifestSha256"]<-JsonValue.Create digest
    let changedSource=JsonNode.Parse(source).AsObject()
    changedSource["verifierRuntimeManifestSha256"]<-JsonValue.Create digest
    let sourceBytes=Encoding.UTF8.GetBytes(changedSource.ToJsonString())
    let sideBytes=Encoding.UTF8.GetBytes(changedSide.ToJsonString())
    let changedReceipt=JsonNode.Parse(receipt).AsObject()
    changedReceipt["sidecarSha256"]<-JsonValue.Create(hash sideBytes)
    changedReceipt["sourceReferenceSha256"]<-JsonValue.Create(hash sourceBytes)
    changedReceipt["verifierRuntimeManifestSha256"]<-JsonValue.Create digest
    File.WriteAllBytes(manifestPath,bytes)
    File.WriteAllBytes(Path.Combine(root,"source-reference.json"),sourceBytes)
    File.WriteAllBytes(Path.Combine(root,"host.json.native-collector.json"),sideBytes)
    File.WriteAllText(receiptPath,changedReceipt.ToJsonString())
let restore ()=
    File.WriteAllBytes(manifestPath,manifest)
    File.WriteAllBytes(Path.Combine(root,"source-reference.json"),source)
    File.WriteAllBytes(Path.Combine(root,"host.json.native-collector.json"),sidecar)
    File.WriteAllBytes(receiptPath,receipt)
let accept ()=
    let code,bytes=InactivePreparation.read configPath
    check(code=0) "valid untouched preparation refused"
    use doc=JsonDocument.Parse bytes
    check(doc.RootElement.GetProperty("nativeAcceptanceClaimed").GetBoolean()=false) "native claim invented"
let refuse ()=let code,_=InactivePreparation.read configPath in check(code=2) "malformed preparation accepted"
try
    accept()
    for name in ["hostConfigSha256";"sidecarSha256";"executableSha256";"credentialReference";"workspaceId";"producerId";"streamId";"grantId";"sourceReferenceSha256";"verifierRuntimeManifestSha256";"sourceVerification";"snapshotOrigin";"sharedCostCompleteness"] do
        let mutated=JsonNode.Parse(receipt).AsObject()
        mutated[name]<-JsonValue.Create "foreign"
        File.WriteAllText(receiptPath,mutated.ToJsonString());refuse();File.WriteAllBytes(receiptPath,receipt)
    for name in ["ownerUid";"grantGeneration"] do
        let mutated=JsonNode.Parse(receipt).AsObject()
        mutated[name]<-JsonValue.Create(-1L)
        File.WriteAllText(receiptPath,mutated.ToJsonString());refuse();File.WriteAllBytes(receiptPath,receipt)
    let activated=JsonNode.Parse(receipt).AsObject()
    activated["activationAuthorized"]<-JsonValue.Create true
    File.WriteAllText(receiptPath,activated.ToJsonString());refuse();File.WriteAllBytes(receiptPath,receipt)
    for path,bytes in [profile,File.ReadAllBytes profile;manifestPath,manifest;Path.Combine(root,"source-reference.json"),source;configPath,config] do
        File.WriteAllBytes(path,Array.append bytes [|byte ' '|]);refuse();File.WriteAllBytes(path,bytes)
    File.WriteAllText(configPath,"{\"Schema\":\"fsgg.telemetry.host-config/2\",\"Schema\":\"fsgg.telemetry.host-config/2\",\"Credentials\":[]}")
    refuse();File.WriteAllBytes(configPath,config)
    // Rebind all outer hashes so these reach inventory validation rather than a hash guard.
    let omitted=JsonNode.Parse(manifest).AsObject()
    let entries=omitted["files"].AsArray()
    let requiredIndex=entries|>Seq.findIndex(fun row->row["path"].GetValue<string>()=executable)
    entries.RemoveAt requiredIndex
    installChangedManifest(Encoding.UTF8.GetBytes(omitted.ToJsonString()));refuse();restore()
    let emptyRequired=JsonNode.Parse(manifest).AsObject()
    let entry=emptyRequired["files"].AsArray()|>Seq.find(fun row->row["path"].GetValue<string>()=executable)
    entry["bytes"]<-JsonValue.Create 0L
    entry["sha256"]<-JsonValue.Create(hash [||])
    installChangedManifest(Encoding.UTF8.GetBytes(emptyRequired.ToJsonString()));refuse();restore()
    let invalid=JsonNode.Parse(manifest).AsObject()
    invalid["files"].AsArray()[0]["sha256"]<-JsonValue.Create "invalid"
    installChangedManifest(Encoding.UTF8.GetBytes(invalid.ToJsonString()));refuse();restore()
    File.SetUnixFileMode(configPath,UnixFileMode.UserRead);refuse();File.SetUnixFileMode(configPath,privateMode)
    File.SetUnixFileMode(empties[0],privateMode);refuse();File.SetUnixFileMode(empties[0],enum<UnixFileMode>0x124)
    File.Move(profile,profile+".saved");File.CreateSymbolicLink(profile,profile+".saved")|>ignore
    refuse();File.Delete profile;File.Move(profile+".saved",profile)
    accept()
    check(not(File.Exists sentinel)) "input-selected process executed"
    check(not(File.Exists(Path.Combine(root,"host.lock")))) "Host lock created"
    check(Directory.GetFileSystemEntries(home).Length=0) "Codex home mutated"
    check(Directory.GetFileSystemEntries(evidence).Length=1) "capture/result/store evidence invented"
    printfn "PASS inactive reader byte joins, custody, duplicates, legitimate empty dependencies and no-launch sentinel"
finally
    File.SetUnixFileMode(secret,privateMode)
    Directory.Delete(root,true)
