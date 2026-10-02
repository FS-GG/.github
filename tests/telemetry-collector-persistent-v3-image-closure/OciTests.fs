module OciTests

open System
open System.IO
open System.Formats.Tar
open System.Text
open System.Text.Json
open System.Threading
open FSGG.Telemetry.PersistentV3.ImageClosure

let private require condition message=if not condition then raise(InvalidOperationException message)
let private tarEntries (entries:(string*byte array*int*int*int) array)=
    use memory=new MemoryStream()
    use writer=new TarWriter(memory,true)
    for name,bytes,uid,gid,mode in entries do
        let entry=PaxTarEntry(TarEntryType.RegularFile,name)
        entry.Uid<-uid;entry.Gid<-gid;entry.Mode<-enum<UnixFileMode> mode
        entry.ModificationTime<-DateTimeOffset.FromUnixTimeSeconds 1790899200L
        entry.DataStream<-new MemoryStream(bytes)
        writer.WriteEntry entry
        entry.DataStream.Dispose()
    writer.Dispose()
    memory.ToArray()
let archive path files configLabel architecture missingBlob wrongSize ambiguous invalidLayerPath=
    let rows=files|>Array.map(fun(name,bytes,mode)->(if invalidLayerPath then "../escaped" else name),bytes,32768,32768,mode)
    let layer=tarEntries rows
    let layerDigest="sha256:"+OciEvidence.shaBytes layer
    let config=JsonSerializer.SerializeToUtf8Bytes {|architecture=architecture;os="linux";config={|User="32768:32768";Labels=Map.ofList["controlled",configLabel]|};rootfs={|``type``="layers";diff_ids=[|layerDigest|]|}|}
    let configDigest="sha256:"+OciEvidence.shaBytes config
    let descriptor media digest size={|mediaType=media;digest=digest;size=size|}
    let layerDescriptor=descriptor "application/vnd.oci.image.layer.v1.tar" layerDigest (int64 layer.Length+(if wrongSize then 1L else 0L))
    let manifest=JsonSerializer.SerializeToUtf8Bytes {|schemaVersion=2;mediaType="application/vnd.oci.image.manifest.v1+json";config=descriptor "application/vnd.oci.image.config.v1+json" configDigest (int64 config.Length);layers=[|layerDescriptor|]|}
    let manifestDigest="sha256:"+OciEvidence.shaBytes manifest
    let manifestDescriptor=descriptor "application/vnd.oci.image.manifest.v1+json" manifestDigest (int64 manifest.Length)
    let index=JsonSerializer.SerializeToUtf8Bytes {|schemaVersion=2;manifests=(if ambiguous then [|manifestDescriptor;manifestDescriptor|] else [|manifestDescriptor|])|}
    let entries=[|"oci-layout",Encoding.UTF8.GetBytes "{\"imageLayoutVersion\":\"1.0.0\"}";"index.json",index;"blobs/sha256/"+manifestDigest.Substring 7,manifest;"blobs/sha256/"+configDigest.Substring 7,config|]
    let entries=if missingBlob then entries else Array.append entries [|"blobs/sha256/"+layerDigest.Substring 7,layer|]
    File.WriteAllBytes(path,tarEntries(entries|>Array.map(fun(name,bytes)->name,bytes,0,0,0o444)))
    manifestDigest,configDigest
let layeredArchive path layerFiles reverseDescriptors =
    let layers=layerFiles|>Array.map(fun files->tarEntries(files|>Array.map(fun(name,bytes,mode)->name,bytes,32768,32768,mode)))
    let digests=layers|>Array.map(fun bytes->"sha256:"+OciEvidence.shaBytes bytes)
    let config=JsonSerializer.SerializeToUtf8Bytes {|architecture="amd64";os="linux";config={|User="32768:32768"|};rootfs={|``type``="layers";diff_ids=digests|}|}
    let descriptor media digest size={|mediaType=media;digest=digest;size=size|}
    let descriptors=Array.map2(fun (bytes:byte array) (digest:string)->descriptor "application/vnd.oci.image.layer.v1.tar" digest (int64 bytes.Length)) layers digests
    let configDigest="sha256:"+OciEvidence.shaBytes config
    let manifest=JsonSerializer.SerializeToUtf8Bytes {|schemaVersion=2;config=descriptor "application/vnd.oci.image.config.v1+json" configDigest (int64 config.Length);layers=(if reverseDescriptors then Array.rev descriptors else descriptors)|}
    let manifestDigest="sha256:"+OciEvidence.shaBytes manifest
    let index=JsonSerializer.SerializeToUtf8Bytes {|schemaVersion=2;manifests=[|descriptor "application/vnd.oci.image.manifest.v1+json" manifestDigest (int64 manifest.Length)|]|}
    let entries=Array.concat[[|"oci-layout",Encoding.UTF8.GetBytes "{\"imageLayoutVersion\":\"1.0.0\"}";"index.json",index;"blobs/sha256/"+manifestDigest.Substring 7,manifest;"blobs/sha256/"+configDigest.Substring 7,config|];Array.map2(fun (bytes:byte array) (digest:string)->"blobs/sha256/"+digest.Substring 7,bytes)layers digests]
    File.WriteAllBytes(path,tarEntries(entries|>Array.map(fun(name,bytes)->name,bytes,0,0,0o444)))
let rejects name action=
    try action();raise(InvalidOperationException("accepted hostile OCI "+name)) with :? InvalidDataException | :? IOException -> ()
let run selected prepared trusted =
    let root=Directory.CreateTempSubdirectory("p2c4-synthetic-oci-").FullName
    let files=selected.Files|>Array.map(fun row->row.TargetPath.TrimStart('/'),File.ReadAllBytes(Path.Combine(selected.AcquisitionRoot,row.SourcePath)),Convert.ToInt32(row.Mode,8))
    let original=Path.Combine(root,"final.oci.tar")
    let manifestDigest,configDigest=archive original files "original" "amd64" false false false false
    let result=OciEvidence.read CancellationToken.None original (Path.Combine(root,"read-original"))
    OciEvidence.verifySelected selected result
    let changed=Path.Combine(root,"changed.oci.tar")
    archive changed files "changed-config-same-files" "amd64" false false false false|>ignore
    let changedResult=OciEvidence.read CancellationToken.None changed (Path.Combine(root,"read-changed"))
    require(result.Inventory=changedResult.Inventory&&not(OciEvidence.equivalent result changedResult)) "same-filesystem changed config was equal"
    for name,missing,size,ambiguous,escape,arch in ["missing",true,false,false,false,"amd64";"size",false,true,false,false,"amd64";"platform",false,false,true,false,"amd64";"escape",false,false,false,true,"amd64";"arch",false,false,false,false,"arm64"] do
        let path=Path.Combine(root,name+".tar")
        archive path files "original" arch missing size ambiguous escape|>ignore
        rejects name (fun()->OciEvidence.read CancellationToken.None path (Path.Combine(root,"read-"+name))|>ignore)
    let truncated=Path.Combine(root,"truncated.tar")
    let raw=File.ReadAllBytes original
    File.WriteAllBytes(truncated,raw[0..700])
    rejects "truncated" (fun()->OciEvidence.read CancellationToken.None truncated (Path.Combine(root,"read-truncated"))|>ignore)
    let bytes (text:string)=Encoding.UTF8.GetBytes text
    let layered=Path.Combine(root,"whiteout.tar")
    let layerFiles=[|[|"data/old",bytes "old",0o444;"data/kept",bytes "old-kept",0o444|];[|"data/.wh..wh..opq",[||],0o000;"data/kept",bytes "new",0o444|]|]
    layeredArchive layered layerFiles false
    let layeredResult=OciEvidence.read CancellationToken.None layered (Path.Combine(root,"read-whiteout"))
    require(layeredResult.Inventory.Length=1&&layeredResult.Inventory[0].Path="/data/kept"&&layeredResult.Inventory[0].Sha256=OciEvidence.shaBytes(bytes "new")) "opaque whiteout did not remove lower-layer entries before same-layer additions"
    let flattened=Path.Combine(root,"flattened.tar")
    layeredArchive flattened [|[|"data/kept",bytes "new",0o444|]|] false
    let flattenedResult=OciEvidence.read CancellationToken.None flattened (Path.Combine(root,"read-flat"))
    require(layeredResult.Inventory=flattenedResult.Inventory&&not(OciEvidence.equivalent layeredResult flattenedResult)) "same filesystem with distinct ordered layers compared equal"
    let reversed=Path.Combine(root,"reversed.tar")
    layeredArchive reversed layerFiles true
    rejects "reversed layers" (fun()->OciEvidence.read CancellationToken.None reversed (Path.Combine(root,"read-reversed"))|>ignore)
    let selectionPath=Path.Combine(root,"selection.json")
    let trustedPath=Path.Combine(root,"trusted.json")
    let authorityPath=Path.Combine(selected.ManifestRoot,"synthetic-native-inventory.json")
    File.WriteAllText(authorityPath,"{\"syntheticOnly\":true}")
    let selected={selected with RuntimeImageDigest=manifestDigest;NativeInventoryPath=Path.GetFileName authorityPath}
    let prepared=match ImageClosure.prepare selected with ClosureResult.Prepared value->value|value->raise(InvalidOperationException(sprintf "synthetic prepare: %A" value))
    File.WriteAllBytes(selectionPath,JsonSerializer.SerializeToUtf8Bytes selected)
    File.WriteAllBytes(trustedPath,JsonSerializer.SerializeToUtf8Bytes trusted)
    let expected=RootlessPolicy.inputIdentity selected selectionPath trustedPath original prepared
    let inputs={Selection=selected;Trusted=trusted;SelectionPath=selectionPath;TrustedPath=trustedPath;ParentOci=original;WorkRoot=Path.Combine(root,"work");EvidenceRoot=Path.Combine(root,"evidence");ExpectedInput=expected;Prepared=prepared}
    let fixtureScript=Path.Combine(root,"synthetic-podman.py")
    File.WriteAllText(fixtureScript,"""import json, os, shutil, sys, time
args=sys.argv[1:]
assert '--remote=false' in args and '--storage-driver=vfs' in args
assert os.environ.get('CONTAINER_HOST') is None
if 'save' in args:
    shutil.copyfile(os.environ['FIXTURE_OCI'],args[args.index('--output')+1])
elif 'inspect' in args:
    print(json.dumps([{'Digest':os.environ['FIXTURE_MANIFEST'],'Id':os.environ['FIXTURE_CONFIG'],'Os':'linux','Architecture':'amd64'}]))
elif 'build' in args:
    assert '--timestamp' in args and '--source-date-epoch' not in args
    assert '--no-cache' in args and args[args.index('--network')+1]=='none'
    time.sleep(.35)
elif 'run' in args:
    entry=args[args.index('--entrypoint')+1]
    if entry.endswith('/dotnet'): print('Microsoft.NETCore.App 10.0.12\nMicrosoft.AspNetCore.App 10.0.12')
    elif 'python' in entry: print('Python 3.14.0')
    else: print('codex-cli 0.158.0')
    """)
    let real=RealProcessBackend():>IProcessBackend
    let mutable actualBuilds=0
    let synthetic={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.Start command=
        require(command.Executable=RootlessPolicy.Builder) "mechanism selected arbitrary production executable"
        if command.Arguments|>Array.contains "build" then actualBuilds<-actualBuilds+1
        real.Start {command with Executable="/usr/bin/python3";Arguments=Array.append [|fixtureScript|] command.Arguments;Environment=command.Environment|>Map.add "FIXTURE_OCI" original|>Map.add "FIXTURE_MANIFEST" manifestDigest|>Map.add "FIXTURE_CONFIG" configDigest}}
    let mechanism=RootlessMechanism(inputs,synthetic)
    let final,trace=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 (DateTimeOffset.UtcNow.AddSeconds 30.) CancellationToken.None mechanism (Runner.initialC4 expected)
    require(Runner.c4Ready final&&not(Runner.qualificationAccepted final)&&actualBuilds=2&&trace.Length=12) (sprintf "synthetic actual process/OCI mechanism did not reach narrower C4Ready: %A" final)
    mechanism.SealTerminal(final,trace)
    require(File.Exists(Path.Combine(inputs.EvidenceRoot,"terminal.json"))&&not(Directory.Exists(Path.Combine(inputs.WorkRoot,"bundle-a")))&&not(Directory.Exists(Path.Combine(inputs.WorkRoot,"bundle-b")))) "synthetic terminal evidence or scoped cleanup absent"
    for name in ["a-build.oci.tar";"b-build.oci.tar";"a-reload.oci.tar";"b-reload.oci.tar"] do require(File.Exists(Path.Combine(inputs.EvidenceRoot,name))) "external OCI archive lost during cleanup"
    let failedInputs={inputs with WorkRoot=Path.Combine(root,"unack-work");EvidenceRoot=Path.Combine(root,"unack-evidence")}
    let unacknowledged={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.Start command=
        if command.Arguments|>Array.contains "build" then raise(IOException "synthetic-lost-start-ack")
        synthetic.Start command}
    let failedMechanism=RootlessMechanism(failedInputs,unacknowledged)
    let failed,_=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 (DateTimeOffset.UtcNow.AddSeconds 30.) CancellationToken.None failedMechanism (Runner.initialC4 expected)
    require(not(Runner.c4Ready failed)&&failed.CleanupFailed&&failed.UnknownBuilds=set["a"]&&failed.Owned.Count=2) "unacknowledged child allowed successful cleanup or C4Ready"
    let changedInputs={inputs with WorkRoot=Path.Combine(root,"mismatch-work");EvidenceRoot=Path.Combine(root,"mismatch-evidence")}
    let mismatched={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.Start command=
        let second=(command.Arguments|>Array.exists(fun arg->arg.Contains("bundle-b",StringComparison.Ordinal)))
        if second&&(command.Arguments|>Array.contains "save") then
            real.Start {command with Executable="/usr/bin/python3";Arguments=Array.append [|fixtureScript|] command.Arguments;Environment=command.Environment|>Map.add "FIXTURE_OCI" changed|>Map.add "FIXTURE_MANIFEST" manifestDigest|>Map.add "FIXTURE_CONFIG" configDigest}
        else synthetic.Start command}
    let mismatchMechanism=RootlessMechanism(changedInputs,mismatched)
    let mismatch,_=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 (DateTimeOffset.UtcNow.AddSeconds 30.) CancellationToken.None mismatchMechanism (Runner.initialC4 expected)
    require(not(Runner.c4Ready mismatch)&&mismatch.Phase=Refused&&mismatch.Owned.IsEmpty&&mismatch.Refusal=Some "BuildMismatch") "actual different OCI config did not refuse and clean owned bundles"
    // The unacknowledged backend above demonstrably never spawned its failing build.
    // Only the fixture owns this explicit reconciliation; production may not infer absence.
    for directory in Directory.EnumerateDirectories(failedInputs.WorkRoot,"*",SearchOption.AllDirectories) do File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
    File.Delete authorityPath
    // Only exact test-owned paths; no production namespace, Podman or native executable was invoked.
    Directory.Delete(root,true)

let processBoundaries()=
    let root=Directory.CreateTempSubdirectory("p2c4-synthetic-process-").FullName
    let backend=RealProcessBackend():>IProcessBackend
    let command code limit={Executable="/usr/bin/python3";Arguments=[|"-I";"-S";"-B";"-c";code|];Environment=Map.ofList["PATH","/usr/bin:/bin"];WorkingDirectory=root;OutputLimit=limit}
    use longChild=backend.Start(command "import time; time.sleep(.35); print('CONTROLLED')" 1024)
    let output=longChild.Wait CancellationToken.None
    require(output.ExitCode=0&&output.Stdout.Trim()="CONTROLLED"&&longChild.HasExited) "actual controlled long child did not settle"
    use cancelled=backend.Start(command "import time; time.sleep(60)" 1024)
    use cancellation=new CancellationTokenSource(100)
    let mutable timeoutObserved=false
    try cancelled.Wait cancellation.Token|>ignore with :? OperationCanceledException->timeoutObserved<-true
    use cleanup=new CancellationTokenSource(2000)
    cancelled.Cancel cleanup.Token
    require(timeoutObserved&&cancelled.HasExited) "actual child timeout did not acknowledge cancellation"
    use overflow=backend.Start(command "print('x'*10000)" 64)
    let mutable overflowObserved=false
    try overflow.Wait CancellationToken.None|>ignore with :? IOException->overflowObserved<-true
    overflow.Cancel cleanup.Token
    require(overflowObserved&&overflow.HasExited) "actual bounded output overflow was accepted"
    Directory.Delete(root,true)
