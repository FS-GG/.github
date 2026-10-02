namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open System.Runtime.InteropServices

module private OwnedProcessGroup=
    [<DllImport("libc",SetLastError=true)>]
    extern int getpgid(int pid)
    [<DllImport("libc",SetLastError=true)>]
    extern int kill(int pid,int signal)

type ProcessCommand={Executable:string;Arguments:string array;Environment:Map<string,string>;WorkingDirectory:string;OutputLimit:int}
type ProcessOutput={ExitCode:int;Stdout:string;Stderr:string}
type IOwnedProcess=
    abstract Identity:string
    abstract Wait:CancellationToken->ProcessOutput
    abstract Cancel:CancellationToken->unit
    abstract HasExited:bool
    inherit IDisposable
type IProcessBackend=
    abstract Start:ProcessCommand->IOwnedProcess
    abstract ReadBuilderIdentity:unit->string
    abstract ReadSupervisorIdentity:unit->string
    abstract ReadAvailableCapacity:string->int64

/// The test backend substitutes process mechanics and capacity observations. The CLI always selects this real backend.
type RealProcessBackend()=
    interface IProcessBackend with
        member _.ReadBuilderIdentity()=OciEvidence.shaFile "/usr/sbin/podman"
        member _.ReadSupervisorIdentity()=OciEvidence.shaFile "/usr/bin/setsid"
        member _.ReadAvailableCapacity path=DriveInfo(Path.GetPathRoot path).AvailableFreeSpace
        member _.Start command=
            let start=ProcessStartInfo("/usr/bin/setsid")
            start.ArgumentList.Add "--wait"
            start.ArgumentList.Add "--"
            start.ArgumentList.Add command.Executable
            start.UseShellExecute<-false
            start.RedirectStandardOutput<-true;start.RedirectStandardError<-true;start.RedirectStandardInput<-true
            start.WorkingDirectory<-command.WorkingDirectory
            start.Environment.Clear()
            for KeyValue(key,value) in command.Environment do start.Environment[key]<-value
            for argument in command.Arguments do start.ArgumentList.Add argument
            let child=new Process(StartInfo=start)
            if not(child.Start()) then child.Dispose();raise(IOException "process-not-started")
            child.StandardInput.Close()
            let started=child.StartTime.ToUniversalTime().Ticks
            let groupClock=Stopwatch.StartNew()
            let mutable group=OwnedProcessGroup.getpgid child.Id
            while group<>child.Id&&not child.HasExited&&groupClock.Elapsed<TimeSpan.FromMilliseconds 500. do
                Thread.Sleep 1
                group<-OwnedProcessGroup.getpgid child.Id
            if group<>child.Id then
                if not child.HasExited then child.Kill(true)
                child.Dispose()
                raise(IOException "process-group-acknowledgment-lost")
            let identity = $"pid:{child.Id}:start:{started}:group:{group}:nonce:{Guid.NewGuid():N}"
            let groupExists()=
                if OwnedProcessGroup.kill(-group,0)=0 then true
                else if Marshal.GetLastPInvokeError()=3 then false
                else raise(IOException "process-group-observation-unknown")
            let kill()=
                if groupExists() then
                    let result=OwnedProcessGroup.kill(-group,9)
                    if result<>0&&Marshal.GetLastPInvokeError()<>3 then raise(IOException "process-group-kill-unacknowledged")
            let drain (reader:StreamReader)=Task.Run(fun()->
                let buffer=Array.zeroCreate<char> 4096
                let output=StringBuilder()
                let mutable count=reader.Read(buffer,0,buffer.Length)
                while count>0 do
                    if output.Length+count>command.OutputLimit then kill();raise(IOException "process-output-overflow")
                    output.Append(buffer,0,count)|>ignore
                    count<-reader.Read(buffer,0,buffer.Length)
                output.ToString())
            let stdout=drain child.StandardOutput
            let stderr=drain child.StandardError
            {new IOwnedProcess with
                member _.Identity=identity
                member _.HasExited=child.HasExited&&stdout.IsCompleted&&stderr.IsCompleted&&not(groupExists())
                member _.Wait token=
                    try
                        child.WaitForExitAsync(token).GetAwaiter().GetResult()
                        let output=stdout.WaitAsync(token).GetAwaiter().GetResult()
                        let error=stderr.WaitAsync(token).GetAwaiter().GetResult()
                        if groupExists() then raise(IOException "process-group-descendant-remains")
                        {ExitCode=child.ExitCode;Stdout=output;Stderr=error}
                    with _->kill();reraise()
                member _.Cancel token=
                    kill()
                    child.WaitForExitAsync(token).GetAwaiter().GetResult()
                    // Drains must settle even when they failed due to overflow.
                    try Task.WhenAll([|stdout:>Task;stderr:>Task|]).WaitAsync(token).GetAwaiter().GetResult() with _ when stdout.IsCompleted&&stderr.IsCompleted->()
                    while groupExists() do token.ThrowIfCancellationRequested();Thread.Sleep 1
                    if not child.HasExited then raise(IOException "process-cancellation-unacknowledged")
                member _.Dispose()=child.Dispose()}

type RootlessInputs={Selection:Selection;Trusted:TrustedNativeSelection;SelectionPath:string;TrustedPath:string;ParentOci:string;WorkRoot:string;EvidenceRoot:string;ExpectedInput:string;Prepared:byte array}
type private Bundle={Logical:string;Root:string;Nonce:string;Environment:Map<string,string>;Contexts:string array;Children:Dictionary<string,IOwnedProcess>;mutable Build:IOwnedProcess option;mutable Result:OciResult option;mutable UnacknowledgedChild:bool}

module RootlessPolicy=
    [<Literal>]
    let Builder="/usr/sbin/podman"
    [<Literal>]
    let BuilderSha256="8d5da8eb5f8c944f16e8666ed3d9d11496515cc7fd6c8e490039477dcda0888c"
    [<Literal>]
    let SupervisorSha256="641d86a5f6e7e212592dec9354b0387dc8a7712582098af42bc99430f90a585f"
    let commandPolicy="podman6.1.2;vfs;linux/amd64;timestamp1790899200;gzip6;oci-archive;network-none;pull-never;no-cache;uid32768"
    let private fail reason=raise(InvalidDataException reason)
    let private privateDirectory (path:string)=
        if not(Path.IsPathFullyQualified path)||Path.GetFullPath path<>path||not(System.Text.RegularExpressions.Regex.IsMatch(path,"^/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$"))||Directory.Exists path||File.Exists path then fail "root-not-fresh-absolute"
        let parent=DirectoryInfo(Path.GetDirectoryName path)
        if not parent.Exists||not(String.IsNullOrEmpty parent.LinkTarget)||((File.GetUnixFileMode parent.FullName)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then fail "root-parent"
    let requireRootlessNamespace()=
        let status=File.ReadAllLines "/proc/self/status"
        let uid=status|>Array.find(fun line->line.StartsWith("Uid:",StringComparison.Ordinal))|>fun line->line.Split([|'\t';' '|],StringSplitOptions.RemoveEmptyEntries)
        let gid=status|>Array.find(fun line->line.StartsWith("Gid:",StringComparison.Ordinal))|>fun line->line.Split([|'\t';' '|],StringSplitOptions.RemoveEmptyEntries)
        if uid[1]<>"0"||gid[1]<>"0" then fail "rootless-namespace-uid0-required"
        let mapping path=File.ReadAllLines path|>Array.map(fun line->line.Split([|'\t';' '|],StringSplitOptions.RemoveEmptyEntries)|>Array.map Int64.Parse)
        for rows in [mapping "/proc/self/uid_map";mapping "/proc/self/gid_map"] do
            if not(rows|>Array.exists(fun row->row.Length=3&&row[0]=0L&&row[1]>0L&&row[2]=1L))||not(rows|>Array.exists(fun row->row.Length=3&&row[0]<=32768L&&row[0]+row[2]>32768L)) then fail "rootless-namespace-mapping"
    let requirePrivateInput path=
        let file=FileInfo path
        if not file.Exists||not(String.IsNullOrEmpty file.LinkTarget)||ImageClosure.ownerUid path<>0||((File.GetUnixFileMode path)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then fail "root-selected-input-custody"
        let parent=file.Directory
        if not parent.Exists||not(String.IsNullOrEmpty parent.LinkTarget)||ImageClosure.ownerUid parent.FullName<>0||((File.GetUnixFileMode parent.FullName)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then fail "root-selected-parent-custody"
    let inputIdentity (selection:Selection) selectionPath trustedPath parent prepared=
        let hashes=Array.concat[[|OciEvidence.shaFile selectionPath;OciEvidence.shaFile trustedPath;OciEvidence.shaFile parent;OciEvidence.shaBytes prepared;commandPolicy;BuilderSha256;SupervisorSha256|];selection.RoleManifests|>Array.map(fun item->OciEvidence.shaFile(Path.Combine(selection.ManifestRoot,item.Path)));[|OciEvidence.shaFile(Path.Combine(selection.ManifestRoot,selection.NativeInventoryPath))|]]
        OciEvidence.shaBytes(Encoding.UTF8.GetBytes(String.concat "\n" hashes))
    let select selection trusted selectionPath trustedPath parent work evidence prepared=
        if selection.IdentityClass<>"production" then fail "run-requires-production-identity"
        requireRootlessNamespace()
        for path in [selectionPath;trustedPath;parent] do requirePrivateInput path
        privateDirectory work;privateDirectory evidence
        if work=evidence||work.StartsWith(evidence+"/",StringComparison.Ordinal)||evidence.StartsWith(work+"/",StringComparison.Ordinal) then fail "roots-overlap"
        {Selection=selection;Trusted=trusted;SelectionPath=selectionPath;TrustedPath=trustedPath;ParentOci=parent;WorkRoot=work;EvidenceRoot=evidence;ExpectedInput=inputIdentity selection selectionPath trustedPath parent prepared;Prepared=prepared}

/// One acknowledged bundle owns context, build store, reload store and all subprocess descendants.
type RootlessMechanism(inputs:RootlessInputs,backend:IProcessBackend)=
    let bundles=Dictionary<string,Bundle>()
    let mutable comparisonSealed=false
    let require condition reason=if not condition then raise(InvalidDataException reason)
    let checkBuilder (token:CancellationToken)=
        token.ThrowIfCancellationRequested()
        require(backend.ReadBuilderIdentity()=RootlessPolicy.BuilderSha256) "builder-identity-changed"
        require(backend.ReadSupervisorIdentity()=RootlessPolicy.SupervisorSha256) "supervisor-identity-changed"
        token.ThrowIfCancellationRequested()
    let seal name value=
        require(String.IsNullOrEmpty(DirectoryInfo(inputs.EvidenceRoot).LinkTarget)&&ImageClosure.ownerUid inputs.EvidenceRoot=inputs.Selection.OwnerUid) "evidence-custody-changed"
        let path=Path.Combine(inputs.EvidenceRoot,name)
        let bytes=JsonSerializer.SerializeToUtf8Bytes value
        use file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)
        file.Write bytes;file.Flush(true)
        File.SetUnixFileMode(path,UnixFileMode.UserRead)
    let validate (token:CancellationToken)=
        token.ThrowIfCancellationRequested()
        match ImageClosure.prepareWithTrustedNative (Some inputs.Trusted) inputs.Selection with
        | Prepared bytes->require(bytes=inputs.Prepared) "prepared-input-changed"
        | _->raise(InvalidDataException "closed-input-no-longer-admitted")
        require(RootlessPolicy.inputIdentity inputs.Selection inputs.SelectionPath inputs.TrustedPath inputs.ParentOci inputs.Prepared=inputs.ExpectedInput) "input-bytes-changed"
        token.ThrowIfCancellationRequested()
    let globalArgs (bundle:Bundle) reload=
        let prefix=if reload then "reload-" else "build-"
        [|"--remote=false";"--events-backend=none";"--storage-driver=vfs";"--root";Path.Combine(bundle.Root,prefix+"graph");"--runroot";Path.Combine(bundle.Root,prefix+"run");"--tmpdir";Path.Combine(bundle.Root,prefix+"tmp")|]
    let validateContext (bundle:Bundle) (token:CancellationToken)=
        let context=bundle.Contexts[0]
        let expected=inputs.Selection.Files|>Array.map _.SourcePath|>Set.ofArray|>Set.add "Containerfile"
        let rec census directory = seq {
            let info=DirectoryInfo directory
            require(String.IsNullOrEmpty info.LinkTarget&&ImageClosure.ownerUid directory=inputs.Selection.OwnerUid) "context-directory-custody"
            for entry in info.EnumerateFileSystemInfos() do
                token.ThrowIfCancellationRequested()
                require(String.IsNullOrEmpty entry.LinkTarget) "context-link"
                if entry.Attributes.HasFlag FileAttributes.Directory then yield! census entry.FullName
                else yield Path.GetRelativePath(context,entry.FullName) }
        require(Set.ofSeq(census context)=expected) "context-census-changed"
        for row in inputs.Selection.Files do
            token.ThrowIfCancellationRequested()
            let file=Path.Combine(context,row.SourcePath)
            require((FileInfo file).Length=row.Bytes&&OciEvidence.shaFile file=row.Sha256&&int(File.GetUnixFileMode file)=Convert.ToInt32(row.Mode,8)&&ImageClosure.ownerUid file=inputs.Selection.OwnerUid&&File.GetLastWriteTimeUtc file=DateTimeOffset.FromUnixTimeSeconds(1790899200L).UtcDateTime) "context-metadata-or-bytes-changed"
        use prepared=JsonDocument.Parse inputs.Prepared
        require(File.ReadAllText(Path.Combine(context,"Containerfile"))=prepared.RootElement.GetProperty("containerfile").GetString()) "context-recipe-changed"
        token.ThrowIfCancellationRequested()
    let start (bundle:Bundle) reload arguments (token:CancellationToken)=
        checkBuilder token
        let command={Executable=RootlessPolicy.Builder;Arguments=Array.append(globalArgs bundle reload)arguments;Environment=bundle.Environment;WorkingDirectory=bundle.Root;OutputLimit=1024*1024}
        let ordinal=bundle.Children.Count+1
        seal ($"{bundle.Logical}-command-{ordinal}.json") {|selectedCommand=command;supervisor="/usr/bin/setsid";supervisorArguments=Array.concat[[|"--wait";"--";command.Executable|];command.Arguments];supervisorSha256=RootlessPolicy.SupervisorSha256|}
        token.ThrowIfCancellationRequested()
        bundle.UnacknowledgedChild<-true
        let child=backend.Start command
        require(not(String.IsNullOrWhiteSpace child.Identity)&&not(bundle.Children.ContainsKey child.Identity)) "process-identity-not-unique"
        bundle.Children.Add(child.Identity,child)
        bundle.UnacknowledgedChild<-false
        seal ($"{bundle.Logical}-process-{ordinal}.json") {|identity=child.Identity;input=inputs.ExpectedInput|}
        child
    let invoke bundle reload arguments token=
        let child=start bundle reload arguments token
        let output=child.Wait token
        seal ($"{bundle.Logical}-output-{bundle.Children.Count}.json") output
        require(output.ExitCode=0) "podman-command-failed"
        require child.HasExited "process-not-quiescent"
        output.Stdout
    let inspect bundle reload token=
        let raw=invoke bundle reload [|"image";"inspect";"localhost/learn-p2c4:inert"|] token
        use doc=JsonDocument.Parse raw
        require(doc.RootElement.ValueKind=JsonValueKind.Array&&doc.RootElement.GetArrayLength()=1) "reload-image-ambiguous"
        let item=doc.RootElement[0]
        let result=bundle.Result.Value
        require(item.GetProperty("Digest").GetString()=result.ManifestDigest&&item.GetProperty("Id").GetString()=result.ConfigDigest&&item.GetProperty("Os").GetString()="linux"&&item.GetProperty("Architecture").GetString()="amd64") "reload-identity-changed"
    let export bundle reload label token=
        let archive=Path.Combine(inputs.EvidenceRoot,bundle.Logical+"-"+label+".oci.tar")
        invoke bundle reload [|"save";"--format";"oci-archive";"--output";archive;"localhost/learn-p2c4:inert"|] token|>ignore
        let result=OciEvidence.read token archive (Path.Combine(bundle.Root,"oci-"+label))
        OciEvidence.verifySelected inputs.Selection result
        File.SetUnixFileMode(archive,UnixFileMode.UserRead)
        result,archive
    let probe bundle executable arguments token=
        // --rm is scoped to the acknowledged bundle; named IDs remain enumerable there on interruption.
        let name="learn-p2c4-"+bundle.Nonce
        invoke bundle true (Array.concat[[|"run";"--name";name;"--rm";"--network";"none";"--read-only";"--cap-drop";"ALL";"--security-opt";"no-new-privileges";"--cpus";"1";"--memory";"512m";"--pids-limit";"64";"--user";"32768:32768";"--entrypoint";executable;"localhost/learn-p2c4:inert"|];arguments]) token
    let cleanup (bundle:Bundle) (token:CancellationToken)=
        require(not bundle.UnacknowledgedChild) "cleanup-unacknowledged-child"
        require(bundle.Children.Values|>Seq.forall _.HasExited) "cleanup-outstanding-child"
        for reload in [false;true] do
            let containers=invoke bundle reload [|"ps";"--all";"--quiet";"--no-trunc"|] token
            for id in containers.Split('\n',StringSplitOptions.RemoveEmptyEntries) do
                require(System.Text.RegularExpressions.Regex.IsMatch(id,"^[0-9a-f]{64}$")) "cleanup-container-identity"
                invoke bundle reload [|"rm";"--force";id|] token|>ignore
            require(String.IsNullOrWhiteSpace(invoke bundle reload [|"ps";"--all";"--quiet"|] token)) "cleanup-containers-remain"
            let mounts=invoke bundle reload [|"mount";"--notruncate"|] token
            require(String.IsNullOrWhiteSpace mounts) "cleanup-mounts-remain"
        require(bundle.Children.Values|>Seq.forall _.HasExited) "cleanup-process-remains"
        require(File.ReadAllText(Path.Combine(bundle.Root,"owner"))=bundle.Nonce) "cleanup-owner-changed"
        token.ThrowIfCancellationRequested()
        for directory in Directory.EnumerateDirectories(bundle.Contexts[0],"*",SearchOption.AllDirectories) do
            require(String.IsNullOrEmpty(DirectoryInfo(directory).LinkTarget)) "cleanup-context-link"
            File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        File.SetUnixFileMode(bundle.Contexts[0],UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        Directory.Delete(bundle.Root,true)
        require(not(Directory.Exists bundle.Root)) "cleanup-resource-remains"
        for child in bundle.Children.Values do child.Dispose()
        bundle.Children.Clear()
    member _.SealTerminal(state:State,trace:(Effect*Observation) list)=
        require(Runner.c4Ready state&&comparisonSealed) "terminal-not-c4-ready"
        seal "terminal.json" {|schema="fsgg.telemetry.persistent-v3-c4-result/1";status="C4Ready";input=inputs.ExpectedInput;qualificationAccepted=false;cleanupObserved=state.Owned.IsEmpty&&state.Running.IsEmpty;effects=trace|>List.map(fun(effect,observation)->sprintf "%A -> %A" effect observation)|}
    interface IRunnerMechanism with
        member _.Execute(effect,token)=Task.Run<Observation>((fun()->
            token.ThrowIfCancellationRequested()
            match effect with
            | AcquireInputs expected->
                require(expected=inputs.ExpectedInput) "input-identity"
                validate token;checkBuilder token
                require(backend.ReadAvailableCapacity(inputs.WorkRoot)>=128L*1024L*1024L*1024L) "capacity-reserve-unavailable"
                token.ThrowIfCancellationRequested()
                Directory.CreateDirectory(inputs.WorkRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
                Directory.CreateDirectory(inputs.EvidenceRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
                let parent=OciEvidence.read token inputs.ParentOci (Path.Combine(inputs.WorkRoot,"parent-admission"))
                require(parent.ManifestDigest=inputs.Selection.RuntimeImageDigest) "parent-manifest-mismatch"
                seal "input.json" {|schema="fsgg.telemetry.persistent-v3-c4-input/1";input=expected;parent=parent.ManifestDigest;builder=RootlessPolicy.BuilderSha256;supervisor=RootlessPolicy.SupervisorSha256;commandPolicy=RootlessPolicy.commandPolicy|}
                InputsAcquired expected
            | ValidateInputs expected->validate token;InputsValidated inputs.ExpectedInput
            | CreateStore logical->
                require((logical="a"||logical="b")&&not(bundles.ContainsKey logical)) "store-logical"
                let root=Path.Combine(inputs.WorkRoot,"bundle-"+logical)
                require(not(Directory.Exists root)) "store-not-fresh"
                Directory.CreateDirectory(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
                let nonce=Guid.NewGuid().ToString("N")
                File.WriteAllText(Path.Combine(root,"owner"),nonce)
                let directories=[|"build-graph";"build-run";"build-tmp";"reload-graph";"reload-run";"reload-tmp";"context";"runtime";"tmp"|]
                for name in directories do token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.Combine(root,name))|>ignore
                File.WriteAllText(Path.Combine(root,"auth.json"),"{\"auths\":{}}")
                File.WriteAllText(Path.Combine(root,"containers.conf"),"[engine]\nevents_logger=\"none\"\n")
                File.WriteAllText(Path.Combine(root,"storage.conf"),$"[storage]\ndriver=\"vfs\"\ngraphroot=\"{root}/build-graph\"\nrunroot=\"{root}/build-run\"\n")
                let env=Map.ofList["PATH","/usr/sbin:/usr/bin:/bin";"HOME",root;"XDG_RUNTIME_DIR",Path.Combine(root,"runtime");"TMPDIR",Path.Combine(root,"tmp");"REGISTRY_AUTH_FILE",Path.Combine(root,"auth.json");"CONTAINERS_CONF",Path.Combine(root,"containers.conf");"CONTAINERS_STORAGE_CONF",Path.Combine(root,"storage.conf")]
                let context=Path.Combine(root,"context")
                for row in inputs.Selection.Files do
                    token.ThrowIfCancellationRequested()
                    let target=Path.Combine(context,row.SourcePath)
                    Directory.CreateDirectory(Path.GetDirectoryName target)|>ignore
                    File.Copy(Path.Combine(inputs.Selection.AcquisitionRoot,row.SourcePath),target,false)
                    File.SetUnixFileMode(target,enum<UnixFileMode>(Convert.ToInt32(row.Mode,8)))
                    File.SetLastWriteTimeUtc(target,DateTimeOffset.FromUnixTimeSeconds(1790899200L).UtcDateTime)
                use prepared=JsonDocument.Parse inputs.Prepared
                File.WriteAllText(Path.Combine(context,"Containerfile"),prepared.RootElement.GetProperty("containerfile").GetString())
                File.SetUnixFileMode(Path.Combine(context,"Containerfile"),UnixFileMode.UserRead|||UnixFileMode.GroupRead|||UnixFileMode.OtherRead)
                File.SetLastWriteTimeUtc(Path.Combine(context,"Containerfile"),DateTimeOffset.FromUnixTimeSeconds(1790899200L).UtcDateTime)
                for directory in Directory.EnumerateDirectories(context,"*",SearchOption.AllDirectories)|>Seq.sortDescending do
                    File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserExecute|||UnixFileMode.GroupRead|||UnixFileMode.GroupExecute|||UnixFileMode.OtherRead|||UnixFileMode.OtherExecute)
                    Directory.SetLastWriteTimeUtc(directory,DateTimeOffset.FromUnixTimeSeconds(1790899200L).UtcDateTime)
                File.SetUnixFileMode(context,UnixFileMode.UserRead|||UnixFileMode.UserExecute|||UnixFileMode.GroupRead|||UnixFileMode.GroupExecute|||UnixFileMode.OtherRead|||UnixFileMode.OtherExecute)
                Directory.SetLastWriteTimeUtc(context,DateTimeOffset.FromUnixTimeSeconds(1790899200L).UtcDateTime)
                let bundle={Logical=logical;Root=root;Nonce=nonce;Environment=env;Contexts=[|context|];Children=Dictionary();Build=None;Result=None;UnacknowledgedChild=false}
                bundles.Add(logical,bundle)
                StoreCreated(logical,root+"#"+nonce)
            | StartBuild(logical,expected)->
                validate token
                require(expected=inputs.ExpectedInput&&(bundles.Values|>Seq.forall(fun b->b.Children.Values|>Seq.forall _.HasExited))) "build-concurrency-or-input"
                let bundle=bundles[logical]
                validateContext bundle token
                invoke bundle false [|"load";"--input";inputs.ParentOci|] token|>ignore
                let inspectRaw=invoke bundle false [|"image";"inspect";inputs.Selection.RuntimeImageDigest|] token
                use parentDoc=JsonDocument.Parse inspectRaw
                require(parentDoc.RootElement.GetArrayLength()=1&&parentDoc.RootElement[0].GetProperty("Digest").GetString()=inputs.Selection.RuntimeImageDigest) "loaded-parent-identity"
                let args=[|"build";"--platform";"linux/amd64";"--format";"oci";"--no-cache";"--pull";"never";"--network";"none";"--isolation";"rootless";"--timestamp";"1790899200";"--compression-format";"gzip";"--compression-level";"6";"--force-compression";"--file";Path.Combine(bundle.Contexts[0],"Containerfile");"--tag";"localhost/learn-p2c4:inert";bundle.Contexts[0]|]
                let child=start bundle false args token
                bundle.Build<-Some child
                BuildStarted(logical,child.Identity)
            | AwaitBuild(logical,identity,expected)->
                let bundle=bundles[logical]
                let child=bundle.Build.Value
                require(child.Identity=identity&&expected=inputs.ExpectedInput) "build-process-stale"
                let output=child.Wait token
                seal (logical+"-build-output.json") output
                require(output.ExitCode=0&&child.HasExited) "build-exit-not-success"
                let result,_=export bundle false "build" token
                bundle.Result<-Some result
                seal (logical+"-build.json") result
                BuildCompleted(logical,identity,result.ManifestDigest,inputs.ExpectedInput)
            | CancelBuild identity->
                let child=bundles.Values|>Seq.collect(fun b->b.Children.Values)|>Seq.find(fun p->p.Identity=identity)
                child.Cancel token
                require child.HasExited "cancel-not-observed"
                BuildCancelled identity
            | CompareBuilds(a,b)->
                let first=bundles["a"]
                let second=bundles["b"]
                require(first.Result.Value.ManifestDigest=a&&second.Result.Value.ManifestDigest=b) "comparison-result-stale"
                if not(OciEvidence.equivalent first.Result.Value second.Result.Value) then BuildsCompared false
                else
                    for bundle in [first;second] do
                        invoke bundle true [|"load";"--input";Path.Combine(inputs.EvidenceRoot,bundle.Logical+"-build.oci.tar")|] token|>ignore
                        inspect bundle true token
                        let reloaded,_=export bundle true "reload" token
                        require(OciEvidence.equivalent bundle.Result.Value reloaded) "reload-evidence-changed"
                        let dotnet=probe bundle "/usr/share/dotnet/dotnet" [|"--list-runtimes"|] token
                        require(dotnet.Contains("Microsoft.NETCore.App 10.0.12")&&dotnet.Contains("Microsoft.AspNetCore.App 10.0.12")) "runtime-probe-version"
                        let python=probe bundle inputs.Trusted.PythonExecutable [|"-I";"-S";"-B";"--version"|] token
                        require(python.Trim()="Python 3.14.0") "python-probe-version"
                        let native=probe bundle "/opt/fsgg/codex/codex" [|"--version"|] token
                        require(native.Trim()="codex-cli 0.158.0") "native-probe-version"
                        seal (bundle.Logical+"-probes.json") {|dotnet=dotnet;python=python;native=native|}
                    token.ThrowIfCancellationRequested()
                    seal "comparison.json" {|schema="fsgg.telemetry.persistent-v3-c4-comparison/1";input=inputs.ExpectedInput;first=first.Result.Value;second=second.Result.Value;ociEqual=true;rawArchivesEqual=first.Result.Value.ArchiveSha256=second.Result.Value.ArchiveSha256;c5Qualification="unknown"|}
                    comparisonSealed<-true
                    BuildsCompared true
            | QualifyInactive _->EffectFailed "c5-served-manager-host-adapter-unavailable"
            | RemoveStore identity->
                let bundle=bundles.Values|>Seq.find(fun b->b.Root+"#"+b.Nonce=identity)
                cleanup bundle token
                StoreAbsent identity),token)
