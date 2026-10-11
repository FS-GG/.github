module OciTests

open System
open System.IO
open System.Formats.Tar
open System.Text
open System.Text.Json
open System.Threading
open System.Runtime.InteropServices
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
    let expected=RootlessPolicy.inputIdentity selected selectionPath trustedPath original prepared (String('c',64))
    let inputs={CustodyIdentity=String('c',64);Selection=selected;Trusted=trusted;SelectionPath=selectionPath;TrustedPath=trustedPath;ParentOci=original;WorkRoot=Path.Combine(root,"work");EvidenceRoot=Path.Combine(root,"evidence");ExpectedInput=expected;Prepared=prepared}
    // This fixture substitutes command effects, not production custody. Actual lease/process
    // qualification belongs to the separately admitted private caller controls.
    let mutable actualBuilds=0
    let runFixture archivePath (command:ProcessCommand)=
        require(command.Executable=RootlessPolicy.Builder) "mechanism selected arbitrary production executable"
        require(command.Arguments|>Array.contains "--remote=false") "remote builder"
        require(command.Environment|>Map.containsKey "CONTAINER_HOST"|>not) "provider environment"
        let has value=command.Arguments|>Array.contains value
        let after value=command.Arguments[Array.findIndex((=)value)command.Arguments+1]
        let mutable output=""
        if has "save" then File.Copy(archivePath,after "--output")
        elif has "inspect" then output<-JsonSerializer.Serialize [|{|Digest=manifestDigest;Id=configDigest;Os="linux";Architecture="amd64"|}|]
        elif has "build" then
            require(has "--timestamp"&&has "--no-cache"&&after "--network"="none") "builder arguments"
            actualBuilds<-actualBuilds+1
        elif has "run" then
            let entry=after "--entrypoint"
            output<-if entry.EndsWith("/dotnet",StringComparison.Ordinal) then "Microsoft.NETCore.App 10.0.12\nMicrosoft.AspNetCore.App 10.0.12" elif entry.Contains "python" then "Python 3.14.0" else "codex-cli 0.158.0"
        let mutable retired=false
        let identity="synthetic:"+Guid.NewGuid().ToString("N")
        {new IOwnedProcess with
            member _.Identity=identity
            member _.Wait token=token.ThrowIfCancellationRequested();retired<-true;{ExitCode=0;Stdout=output;Stderr=""}
            member _.Cancel token=token.ThrowIfCancellationRequested();retired<-true
            member _.HasExited=retired
            member _.Dispose()=()}
    let synthetic={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.ReadAvailableCapacity _=128L*1024L*1024L*1024L
      member _.RetainCommandEvidence _=()
      member _.Start command=runFixture original command}
    // Hosted CI capacity is unrelated to this synthetic process/OCI fixture. Production still
    // reads the real drive and requires the same 128 GiB reserve before acquiring any resources.
    let lowCapacityInputs={inputs with WorkRoot=Path.Combine(root,"low-capacity-work");EvidenceRoot=Path.Combine(root,"low-capacity-evidence")}
    let mutable lowCapacityStarts=0
    let mutable capacityObserved=false
    let lowCapacity={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.ReadAvailableCapacity path=
        require(path=lowCapacityInputs.WorkRoot) "capacity checked unrelated path"
        capacityObserved<-true
        128L*1024L*1024L*1024L-1L
      member _.RetainCommandEvidence _=()
      member _.Start _=
        lowCapacityStarts<-lowCapacityStarts+1
        raise(InvalidOperationException "capacity refusal started a process")}
    let lowCapacityMechanism=RootlessMechanism(lowCapacityInputs,lowCapacity):>IRunnerMechanism
    let mutable capacityRefused=false
    try lowCapacityMechanism.Execute(AcquireInputs expected,CancellationToken.None).GetAwaiter().GetResult()|>ignore
    with :? InvalidDataException as error->
        require(error.Message="capacity-reserve-unavailable") "capacity fixture refused for an unrelated reason"
        capacityRefused<-true
    require(capacityObserved&&capacityRefused&&lowCapacityStarts=0&&not(Directory.Exists lowCapacityInputs.WorkRoot)&&not(Directory.Exists lowCapacityInputs.EvidenceRoot)) "below-reserve capacity acquired resources"
    let mechanism=RootlessMechanism(inputs,synthetic)
    let final,trace=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 (DateTimeOffset.UtcNow.AddSeconds 30.) CancellationToken.None mechanism (Runner.initialC4 expected)
    require(Runner.c4Ready final&&not(Runner.qualificationAccepted final)&&actualBuilds=2&&trace.Length=12) (sprintf "synthetic OCI mechanism did not reach narrower C4Ready: %A" final)
    mechanism.SealTerminal(final,trace)
    require(File.Exists(Path.Combine(inputs.EvidenceRoot,"terminal.json"))&&not(Directory.Exists(Path.Combine(inputs.WorkRoot,"bundle-a")))&&not(Directory.Exists(Path.Combine(inputs.WorkRoot,"bundle-b")))) "synthetic terminal evidence or scoped cleanup absent"
    for name in ["a-build.oci.tar";"b-build.oci.tar";"a-reload.oci.tar";"b-reload.oci.tar"] do require(File.Exists(Path.Combine(inputs.EvidenceRoot,name))) "external OCI archive lost during cleanup"
    let failedInputs={inputs with WorkRoot=Path.Combine(root,"unack-work");EvidenceRoot=Path.Combine(root,"unack-evidence")}
    let unacknowledged={new IProcessBackend with
      member _.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256
      member _.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256
      member _.ReadAvailableCapacity _=128L*1024L*1024L*1024L
      member _.RetainCommandEvidence _=()
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
      member _.ReadAvailableCapacity _=128L*1024L*1024L*1024L
      member _.RetainCommandEvidence _=()
      member _.Start command=
        let second=(command.Arguments|>Array.exists(fun arg->arg.Contains("bundle-b",StringComparison.Ordinal)))
        if second&&(command.Arguments|>Array.contains "save") then
            runFixture changed command
        else synthetic.Start command}
    let mismatchMechanism=RootlessMechanism(changedInputs,mismatched)
    let mismatch,_=RunnerExecution.runWithBudgets RunnerExecution.productionBudgets 32 (DateTimeOffset.UtcNow.AddSeconds 30.) CancellationToken.None mismatchMechanism (Runner.initialC4 expected)
    require(not(Runner.c4Ready mismatch)&&mismatch.Phase=Refused&&mismatch.Owned.IsEmpty&&mismatch.Refusal=Some "BuildMismatch") "different OCI config did not refuse and clean owned bundles"
    // The unacknowledged backend above demonstrably never spawned its failing build.
    // Only the fixture owns this explicit reconciliation; production may not infer absence.
    for directory in Directory.EnumerateDirectories(failedInputs.WorkRoot,"*",SearchOption.AllDirectories) do File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
    File.Delete authorityPath
    // Only exact test-owned paths; no production namespace, Podman or native executable was invoked.
    Directory.Delete(root,true)

let processBoundaries()=
    // Pure schema controls; no legacy process backend or native acceptance.
    let parse text=use doc=JsonDocument.Parse(text:string) in doc.RootElement.Clone()
    let binding=JsonSerializer.Serialize {|schema=CustodyProtocol.Schema;identity=String('a',64);deadline=DateTimeOffset.UtcNow.AddMinutes(1.).ToString("O");admissionSha256=String('b',64)|}
    CustodyProtocol.binding(parse binding)|>ignore
    rejects "extra custody field" (fun()->CustodyProtocol.binding(parse(binding.TrimEnd('}')+",\"extra\":true}"))|>ignore)
    rejects "duplicate custody field" (fun()->CustodyProtocol.binding(parse(binding.TrimEnd('}')+",\"identity\":\"x\"}"))|>ignore)
    let expired=JsonSerializer.Serialize {|schema=CustodyProtocol.Schema;identity=String('a',64);deadline=DateTimeOffset.UtcNow.AddSeconds(-1.).ToString("O");admissionSha256=String('b',64)|}
    rejects "expired original deadline" (fun()->CustodyProtocol.binding(parse expired)|>ignore)
    rejects "recycled numeric identity" (fun()->CustodyProtocol.sha "pid:123:start:456")

    let lease=String('c',64)
    let observation (retired:bool) (stdout:string) (stderr:string)=JsonSerializer.Serialize {|lease=lease;retired=retired;exitCode=7;stdout=stdout;stderr=stderr|}|>parse
    let read node=CustodyProtocol.observation lease 8 node
    let retired,output=read(observation true "abcd" "efgh")
    require(retired&&output.ExitCode=7&&output.Stdout="abcd"&&output.Stderr="efgh") "exact combined output ceiling refused"
    let running,_=read(observation false "" "")
    require(not running) "running acknowledgment manufactured retirement"
    rejects "combined output above ceiling" (fun()->read(observation true "abcde" "fghi")|>ignore)
    rejects "UTF-8 output above ceiling" (fun()->read(observation true "ééééé" "")|>ignore)
    rejects "null output" (fun()->read(observation true null "")|>ignore)
    rejects "unretired output above ceiling" (fun()->read(observation false "123456789" "")|>ignore)
    rejects "foreign process lease" (fun()->CustodyProtocol.observation (String('d',64)) 8 (observation true "" "")|>ignore)
    let extra=(JsonSerializer.Serialize {|lease=lease;retired=true;exitCode=0;stdout="";stderr=""|}).TrimEnd('}')+",\"extra\":true}"
    rejects "extra observation field" (fun()->read(parse extra)|>ignore)
    let duplicate=(JsonSerializer.Serialize {|lease=lease;retired=true;exitCode=0;stdout="";stderr=""|}).TrimEnd('}')+",\"retired\":true}"
    rejects "duplicate observation field" (fun()->read(parse duplicate)|>ignore)
    let refusesPayload name action=
        let refused=try action();false with :? IOException| :? InvalidOperationException| :? FormatException->true
        require refused ("accepted malformed custody payload "+name)
    refusesPayload "nonboolean retirement" (fun()->read(parse("{\"lease\":\""+lease+"\",\"retired\":1,\"exitCode\":0,\"stdout\":\"\",\"stderr\":\"\"}"))|>ignore)
    refusesPayload "nonnumeric exit" (fun()->read(parse("{\"lease\":\""+lease+"\",\"retired\":true,\"exitCode\":\"0\",\"stdout\":\"\",\"stderr\":\"\"}"))|>ignore)
    refusesPayload "nonstring output" (fun()->read(parse("{\"lease\":\""+lease+"\",\"retired\":true,\"exitCode\":0,\"stdout\":1,\"stderr\":\"\"}"))|>ignore)
    printfn "PASS custody observation combined UTF-8 bounds and closed retirement joins"

[<DllImport("libc",EntryPoint="dup",SetLastError=true)>]
extern int private duplicateDescriptor(int descriptor)
[<DllImport("libc",EntryPoint="fcntl",SetLastError=true)>]
extern int private descriptorFlags(int descriptor,int command)

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private CoreReset=delegate of nativeint*unativeint*uint64*uint64->int
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private CoreReceive=delegate of nativeint*unativeint*uint64->int
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private CoreComplete=delegate of int*int*int*nativeint*unativeint*nativeint*unativeint*uint64*nativeint*unativeint->int
let private pinned (bytes:byte array) action=
    let handle=GCHandle.Alloc(bytes,GCHandleType.Pinned)
    try action(handle.AddrOfPinnedObject()) finally handle.Free()

// Exercise the production inherited channel, with an open AF_UNIX peer and no process backend.
let custodyAcknowledgments()=
    let identity=String('a',64)
    let lease=String('c',64)
    let input=String('d',64)
    let parse (text:string)=use doc=JsonDocument.Parse text in doc.RootElement.Clone()
    let json value=JsonSerializer.Serialize value
    let bound=json {|input=input|}
    let started=json {|lease=lease|}
    let terminal=json {|lease=lease;retired=true;exitCode=0;stdout="done";stderr=""|}
    let wrongObservation=json {|lease=String('e',64);retired=true;exitCode=0;stdout="";stderr=""|}
    let mutable coreUnsettled=false
    let withPeer name (replies:(string*string) list) coreFactory exercise=
        printfn "BEGIN inherited custody fixture %s" name
        let root=Directory.CreateTempSubdirectory("p2c4-custody-socket-").FullName
        let path=Path.Combine(root,"peer.sock")
        let oldFd=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_FD"
        let oldBinding=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_BINDING"
        try
            use budget=new CancellationTokenSource(TimeSpan.FromSeconds 5.)
            use listener=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Unspecified)
            listener.Bind(System.Net.Sockets.UnixDomainSocketEndPoint path)
            listener.Listen 1
            use client=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Unspecified)
            client.ConnectAsync(System.Net.Sockets.UnixDomainSocketEndPoint path,budget.Token).AsTask().GetAwaiter().GetResult()
            use peer=listener.AcceptAsync(budget.Token).AsTask().GetAwaiter().GetResult()
            use stream=new System.Net.Sockets.NetworkStream(peer,false)
            let deadline=DateTimeOffset.UtcNow.AddSeconds(5.)
            let binding=JsonSerializer.SerializeToUtf8Bytes {|schema=CustodyProtocol.Schema;identity=identity;deadline=deadline.ToString("O");admissionSha256=String('b',64)|}
            let coreReply=coreFactory|>Option.map(fun factory->factory deadline)
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",Convert.ToBase64String binding)
            // Independent descriptor ownership: invalidating a Socket SafeHandle suppresses its
            // ReleaseHandle, but Socket.Dispose waits for that release and can spin forever.
            let inherited=duplicateDescriptor(client.Handle.ToInt32())
            require(inherited>=3) "fixture descriptor duplication failed"
            use transfer=new Microsoft.Win32.SafeHandles.SafeFileHandle(nativeint inherited,true)
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",inherited.ToString(Globalization.CultureInfo.InvariantCulture))
            transfer.SetHandleAsInvalid() // Only the plain handle transfers; its Dispose does not await Socket.ReleaseHandle.
            use channel=CustodyChannel.BindInherited()
            printfn "BOUND inherited custody fixture %s" name
            let serving=System.Threading.Tasks.Task.Run(fun()->
                for expectedAction,payload in replies do
                    printfn "READ inherited custody fixture %s action %s" name expectedAction
                    let header=Array.zeroCreate<byte> 4
                    stream.ReadExactlyAsync(header.AsMemory(),budget.Token).AsTask().GetAwaiter().GetResult()
                    let count=System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header,0))
                    require(count>0&&count<=2*1024*1024) "fixture request frame bound"
                    let bytes=Array.zeroCreate<byte> count
                    stream.ReadExactlyAsync(bytes.AsMemory(),budget.Token).AsTask().GetAwaiter().GetResult()
                    use request=JsonDocument.Parse bytes
                    let node=request.RootElement
                    require(node.GetProperty("action").GetString()=expectedAction) "fixture received unexpected action"
                    let framed=
                        match coreReply with
                        | Some answer->answer (Array.append header bytes) payload
                        | None->
                            let response=JsonSerializer.SerializeToUtf8Bytes {|schema=CustodyProtocol.Schema;identity=identity;ordinal=node.GetProperty("ordinal").GetInt32();status="acknowledged";payload=parse payload|}
                            Array.append (BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder response.Length)) response
                    stream.WriteAsync(framed.AsMemory(),budget.Token).AsTask().GetAwaiter().GetResult())
            let mutable primaryFailed=false
            try
                try
                    exercise channel
                    serving.WaitAsync(budget.Token).GetAwaiter().GetResult()
                    printfn "EXERCISED inherited custody fixture %s" name
                with _->primaryFailed<-true;reraise()
            finally
                budget.Cancel()
                peer.Dispose()
                // Preserve the first assertion, but retain an unsettled-reader timeout as failure.
                try serving.WaitAsync(TimeSpan.FromSeconds 1.).GetAwaiter().GetResult() with
                | :? TimeoutException as error->
                    if Option.isSome coreReply then coreUnsettled<-true
                    eprintfn "FAIL unsettled inherited custody fixture %s: %s" name error.Message
                    if not primaryFailed then raise error
                | _->()
        finally
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",oldFd)
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",oldBinding)
            Directory.Delete(root,true)
            printfn "CLEANED inherited custody fixture %s" name
    let withChannel name replies exercise=withPeer name replies None exercise
    let refuses expected action=
        let mutable reason=None
        try action() with :? IOException as error->reason<-Some error.Message
        require(reason=Some expected) (sprintf "custody refusal differs: expected %s, actual %A" expected reason)
    let nextRefuses (channel:CustodyChannel)=
        // Peer is still open; a baseline reuse would attempt IO and fail this exact refusal assertion.
        refuses "custody-lost-or-expired" (fun()->channel.Retain [||])
    let command={Executable="/usr/sbin/podman";Arguments=[|"fixture-only-never-executed"|];Environment=Map.empty;WorkingDirectory="/";OutputLimit=1024*1024}
    withChannel "bind" ["bind-input",json {|input=String('e',64)|}] (fun channel->
        refuses "custody-input-join" (fun()->channel.BindInput input)
        nextRefuses channel)
    withChannel "retain" ["bind-input",bound;"retain",json {|charged=1|}] (fun channel->
        channel.BindInput input
        refuses "custody-output-charge" (fun()->channel.Retain [||])
        nextRefuses channel)
    withChannel "start" ["bind-input",bound;"start",json {|lease="invalid"|}] (fun channel->
        channel.BindInput input
        refuses "custody-sha256" (fun()->channel.Start command|>ignore)
        nextRefuses channel)
    withChannel "observe" ["bind-input",bound;"start",started;"observe",wrongObservation] (fun channel->
        channel.BindInput input
        use child=channel.Start command
        refuses "custody-command-join" (fun()->child.Wait CancellationToken.None|>ignore)
        require(not child.HasExited) "malformed observation credited retirement"
        nextRefuses channel)
    withChannel "cancel" ["bind-input",bound;"start",started;"cancel",wrongObservation] (fun channel->
        channel.BindInput input
        use child=channel.Start command
        refuses "custody-command-join" (fun()->child.Cancel CancellationToken.None)
        require(not child.HasExited) "malformed cancellation credited retirement"
        nextRefuses channel)
    withChannel "cached-retirement" ["bind-input",bound;"start",started;"observe",terminal;"retain",json {|charged=1|}] (fun channel->
        channel.BindInput input
        use child=channel.Start command
        let output=child.Wait CancellationToken.None
        require(child.HasExited&&output.Stdout="done") "valid retirement was not observed"
        refuses "custody-output-charge" (fun()->channel.Retain [||])
        require(not child.HasExited) "later poison retained successful custody"
        refuses "custody-lost-or-expired" (fun()->child.Wait CancellationToken.None|>ignore)
        refuses "custody-lost-or-expired" (fun()->child.Cancel CancellationToken.None)
        nextRefuses channel)
    printfn "PASS actual inherited custody poisoning across five actions and cached retirement"
    // Binding refusal must release an already admitted numeric descriptor, even when
    // binding metadata is missing. F_GETFD proves the actual descriptor is closed.
    let rejectsBinding name selected expected=
        printfn "BEGIN inherited custody binding refusal %s" name
        let root=Directory.CreateTempSubdirectory("p2c4-custody-binding-").FullName
        let oldFd=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_FD"
        let oldBinding=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_BINDING"
        try
            use budget=new CancellationTokenSource(TimeSpan.FromSeconds 5.)
            use listener=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Unspecified)
            let endpoint=System.Net.Sockets.UnixDomainSocketEndPoint(Path.Combine(root,"peer.sock"))
            listener.Bind endpoint
            listener.Listen 1
            use client=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Unspecified)
            client.ConnectAsync(endpoint,budget.Token).AsTask().GetAwaiter().GetResult()
            use peer=listener.AcceptAsync(budget.Token).AsTask().GetAwaiter().GetResult()
            let inherited=duplicateDescriptor(client.Handle.ToInt32())
            require(inherited>=3) "binding fixture descriptor duplication failed"
            use transfer=new Microsoft.Win32.SafeHandles.SafeFileHandle(nativeint inherited,true)
            require(descriptorFlags(inherited,1)>=0) "binding fixture descriptor not initially open"
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",inherited.ToString(Globalization.CultureInfo.InvariantCulture))
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",selected)
            transfer.SetHandleAsInvalid()
            refuses expected (fun()->use channel=CustodyChannel.BindInherited() in ())
            let flags=descriptorFlags(inherited,1)
            let error=Marshal.GetLastPInvokeError()
            require(flags = -1&&error=9) "refused binding retained its inherited descriptor"
        finally
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_FD",oldFd)
            Environment.SetEnvironmentVariable("FSGG_LEARN_CUSTODY_BINDING",oldBinding)
            Directory.Delete(root,true)
        printfn "PASS inherited custody binding refusal closes descriptor %s" name
    rejectsBinding "malformed" (Convert.ToBase64String(Encoding.UTF8.GetBytes "{}")) "custody-fields"
    rejectsBinding "missing" null "root-admitted-custody-channel-required"

    // Actual F# serialization/channel framing enters the compiled C parser, and
    // C acknowledgments enter the production F# validator. Results remain synthetic.
    let selected=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY"
    let selectedHash=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY_SHA256"
    require(not(String.IsNullOrWhiteSpace selected)&&Path.IsPathFullyQualified selected) "custody-core-test-library-required"
    CustodyProtocol.sha selectedHash
    require(not(File.GetAttributes(selected).HasFlag FileAttributes.ReparsePoint)&&OciEvidence.shaBytes(File.ReadAllBytes selected)=selectedHash) "custody-core-test-library-bytes"
    // This byte join does not authenticate the compiler; the admitted source
    // operation supplies the independently accepted toolchain/provider profile.
    let library=NativeLibrary.Load selected
    try
        let reset=Marshal.GetDelegateForFunctionPointer<CoreReset>(NativeLibrary.GetExport(library,"fsgg_custody_core_fixture_reset"))
        let receive=Marshal.GetDelegateForFunctionPointer<CoreReceive>(NativeLibrary.GetExport(library,"fsgg_custody_core_fixture_receive"))
        let complete=Marshal.GetDelegateForFunctionPointer<CoreComplete>(NativeLibrary.GetExport(library,"fsgg_custody_core_fixture_complete"))
        let now()=uint64(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        let factory (deadline:DateTimeOffset)=
            let inputBytes=Encoding.UTF8.GetBytes input
            require(pinned inputBytes (fun pointer->reset.Invoke(pointer,unativeint inputBytes.Length,now(),uint64(deadline.ToUnixTimeMilliseconds())))=1) "custody-core-fixture-reset"
            fun (request:byte array) (syntheticPayload:string)->
                require(pinned request (fun pointer->receive.Invoke(pointer,unativeint request.Length,now()))=1) "custody-core-rejected-FSharp-request"
                use result=JsonDocument.Parse syntheticPayload
                let mutable retiredNode=Unchecked.defaultof<JsonElement>
                let observed=result.RootElement.TryGetProperty("retired",&retiredNode)
                let retired=if observed&&retiredNode.GetBoolean() then 1 else 0
                let exitCode=if observed then result.RootElement.GetProperty("exitCode").GetInt32() else 0
                let stdout=Encoding.UTF8.GetBytes(if observed then result.RootElement.GetProperty("stdout").GetString() else "")
                let stderr=Encoding.UTF8.GetBytes(if observed then result.RootElement.GetProperty("stderr").GetString() else "")
                let response=Array.zeroCreate<byte> (2*1024*1024+4)
                let count=pinned stdout (fun outPointer->pinned stderr (fun errPointer->pinned response (fun replyPointer->
                    complete.Invoke(1,retired,exitCode,outPointer,unativeint stdout.Length,errPointer,unativeint stderr.Length,now(),replyPointer,unativeint response.Length))))
                require(count>=4&&count<=response.Length) "custody-core-reply-refused"
                response[..count-1]
        let running=json {|lease=String('d',64);retired=false;exitCode=7;stdout="from-c-é";stderr=""|}
        let finished=json {|lease=String('d',64);retired=true;exitCode=7;stdout="from-c-é";stderr=""|}
        let cCommand={command with Arguments=[|"fixture-only-never-executed";"é\n\"\\"|];Environment=Map.ofList ["HOME","/private/empty";"LANG","C"]}
        withPeer "c-core-wait" ["bind-input",bound;"retain",json {|charged=0|};"start",json {|lease=String('d',64)|};"observe",running;"observe",finished] (Some factory) (fun channel->
            channel.BindInput input
            channel.Retain [||]
            use child=channel.Start cCommand
            let output=child.Wait CancellationToken.None
            require(child.HasExited&&output.ExitCode=7&&output.Stdout="from-c-é") "custody-core-FSharp-wait-join")
        withPeer "c-core-cancel" ["bind-input",bound;"start",json {|lease=String('d',64)|};"cancel",running;"observe",finished] (Some factory) (fun channel->
            channel.BindInput input
            use child=channel.Start cCommand
            child.Cancel CancellationToken.None
            let output=child.Wait CancellationToken.None
            require(child.HasExited&&output.ExitCode=7&&output.Stdout="from-c-é") "custody-core-FSharp-cancel-cached-join")
        printfn "PASS compiled C/FSharp custody wire interoperability; retirement results synthetic"
    finally
        // Never unload code beneath an unobserved test-owned callback.
        if coreUnsettled then eprintfn "FAIL retaining C test library after unsettled peer; cleanup unknown"
        else NativeLibrary.Free library

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type private AdapterControls=delegate of unit->int
let custodyAdapterSourceControls()=
    let selected=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY"
    let selectedHash=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY_SHA256"
    require(not(String.IsNullOrWhiteSpace selected)&&Path.IsPathFullyQualified selected) "custody-adapter-source-library-required"
    CustodyProtocol.sha selectedHash
    require(not(File.GetAttributes(selected).HasFlag FileAttributes.ReparsePoint)&&OciEvidence.shaBytes(File.ReadAllBytes selected)=selectedHash) "custody-adapter-source-library-bytes"
    let library=NativeLibrary.Load selected
    try
        let controls=Marshal.GetDelegateForFunctionPointer<AdapterControls>(NativeLibrary.GetExport(library,"fsgg_custody_adapter_controls"))
        require(controls.Invoke()=0) "actual adapter C source controls failed"
        printfn "PASS FSharp ABI invokes actual adapter source controls; native custody NOT_RUN"
    finally NativeLibrary.Free library

// Invoked only by the admitted fixed C root test controller. These environment
// values describe its frozen test operation; they do not authenticate admission.
// No native setup, Podman, shell, process selection or provisioning happens here.
let custodyAdapterNativeFixture()=
    let kind=Environment.GetEnvironmentVariable "FSGG_LEARN_NATIVE_CASE"
    let input=Environment.GetEnvironmentVariable "FSGG_LEARN_NATIVE_INPUT"
    let cwd=Environment.GetEnvironmentVariable "FSGG_LEARN_NATIVE_CWD"
    require(Set.contains kind (set["normal";"descendant";"denial";"allowance";"overflow";"wrong-command";"disconnect";"partial-ack"])) "native fixture case"
    CustodyProtocol.sha input
    require(Path.IsPathFullyQualified cwd) "native fixture exact directory"
    let workload=if kind="disconnect" then "descendant" elif kind="partial-ack" then "allowance" else kind
    let command={Executable="/usr/sbin/podman";Arguments=[|"--custody-fixture-workload";workload|];Environment=Map.ofList["LC_ALL","C";"PATH","/usr/bin:/bin"];WorkingDirectory=cwd;OutputLimit=1024*1024}
    if kind="partial-ack" then
        // Deliberately read only two bytes of the actual large observation
        // acknowledgment, then disconnect. The C controller bounds SO_SNDBUF;
        // its retained source/terminal evidence must identify actual delivery.
        let fd=Int32.Parse(Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_FD")
        use handle=new System.Net.Sockets.SafeSocketHandle(nativeint fd,true)
        use socket=new System.Net.Sockets.Socket(handle)
        use stream=new System.Net.Sockets.NetworkStream(socket,false)
        let binding=Environment.GetEnvironmentVariable "FSGG_LEARN_CUSTODY_BINDING"|>Convert.FromBase64String
        use selected=JsonDocument.Parse binding
        let deadline=CustodyProtocol.binding selected.RootElement
        let identity=selected.RootElement.GetProperty("identity").GetString()
        let admission=selected.RootElement.GetProperty("admissionSha256").GetString()
        use wait=new CancellationTokenSource(deadline-DateTimeOffset.UtcNow)
        let send ordinal action payload=
            let body=JsonSerializer.SerializeToUtf8Bytes {|schema=CustodyProtocol.Schema;identity=identity;ordinal=ordinal;action=action;payload=payload|}
            let header=BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder body.Length)
            stream.WriteAsync(header.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            stream.WriteAsync(body.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
        let read ordinal=
            let header=Array.zeroCreate<byte> 4
            stream.ReadExactlyAsync(header.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            let count=System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(header,0))
            require(count>0&&count<=2*1024*1024) "native response bound"
            let body=Array.zeroCreate<byte> count
            stream.ReadExactlyAsync(body.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
            use doc=JsonDocument.Parse body
            CustodyProtocol.exact ["schema";"identity";"ordinal";"status";"payload"] doc.RootElement
            require(doc.RootElement.GetProperty("identity").GetString()=identity&&doc.RootElement.GetProperty("ordinal").GetInt32()=ordinal&&doc.RootElement.GetProperty("status").GetString()="acknowledged") "native response join"
            doc.RootElement.GetProperty("payload").Clone()
        send 1 "bind-input" {|input=input;admissionSha256=admission|};read 1|>ignore
        send 2 "start" command
        let started=read 2
        let lease=started.GetProperty("lease").GetString()
        CustodyProtocol.sha lease
        Thread.Sleep 200
        send 3 "observe" {|lease=lease|}
        let partial=Array.zeroCreate<byte> 2
        stream.ReadExactlyAsync(partial.AsMemory(),wait.Token).AsTask().GetAwaiter().GetResult()
        socket.Shutdown(System.Net.Sockets.SocketShutdown.Both)
    else
        use channel=CustodyChannel.BindInherited()
        channel.BindInput input
        if kind="wrong-command" then
            let mutable refused=false
            try
                use unexpected=channel.Start {command with Arguments=[|"unexpected"|];Environment=Map.add "EXTRA" "1" command.Environment;WorkingDirectory=cwd+"/unexpected"}
                ()
            with :? IOException->refused<-true
            require refused "native mismatched command returned a lease"
        else
            use ownedProcess=channel.Start command
            if kind="disconnect" then ()
            elif kind="overflow" then
                let mutable refused=false
                try ownedProcess.Wait CancellationToken.None|>ignore with :? IOException->refused<-true
                require refused "native capture overflow acknowledged"
            elif kind="descendant" then
                Thread.Sleep 300
                require(not ownedProcess.HasExited) "native descendant manufactured retirement"
                ownedProcess.Cancel CancellationToken.None
                let output=ownedProcess.Wait CancellationToken.None
                require(output.Stdout="descendant-live\n"&&ownedProcess.HasExited) "native descendant cancellation/drain"
            else
                let output=ownedProcess.Wait CancellationToken.None
                require(output.ExitCode=0&&ownedProcess.HasExited) "native kernel retirement required"
                match kind with
                | "normal"->require(output.Stdout="native-output\n"&&output.Stderr="native-error\n") "native actual captured output"
                | "denial"->require(output.Stdout="controls-denied\n") "native control/descriptor deny probes"
                | "allowance"->require(output.Stdout=String('x',8192)&&output.Stderr="") "native exact shared allowance"
                | _->failwith "native fixed case"
    printfn "PASS native FSharp client case=%s; root controller must independently prove cleanup" kind
