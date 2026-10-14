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

/// Production commands are released only by the operation caller on the inherited channel.
type RealProcessBackend(custody:CustodyChannel)=
    interface IProcessBackend with
        member _.ReadBuilderIdentity()=OciEvidence.shaFile "/usr/sbin/podman"
        member _.ReadSupervisorIdentity()=OciEvidence.shaFile "/usr/bin/setsid"
        member _.ReadAvailableCapacity path=DriveInfo(Path.GetPathRoot path).AvailableFreeSpace
        member _.RetainCommandEvidence bytes=custody.Retain bytes
        member _.Start command=custody.Start command

type RootlessInputs={CustodyIdentity:string;Selection:Selection;Trusted:TrustedNativeSelection;SelectionPath:string;TrustedPath:string;ParentOci:string;WorkRoot:string;EvidenceRoot:string;ExpectedInput:string;Prepared:byte array;Program:CommandProgram}
type private Bundle={Logical:string;Root:string;Nonce:string;Environment:Map<string,string>;Contexts:string array;Children:Dictionary<string,IOwnedProcess>;mutable Build:IOwnedProcess option;mutable Result:OciResult option;mutable UnacknowledgedChild:bool;Gate:obj;mutable C5Active:bool;mutable C5Retired:bool;mutable C5Residue:bool}

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
    let inputIdentity (selection:Selection) selectionPath trustedPath parent prepared custodyIdentity program=
        let hashes=Array.concat[[|OciEvidence.shaFile selectionPath;OciEvidence.shaFile trustedPath;OciEvidence.shaFile parent;OciEvidence.shaBytes prepared;custodyIdentity;CommandRecipe.identity program;commandPolicy;InactiveQualification.Policy;BuilderSha256;SupervisorSha256|];selection.RoleManifests|>Array.map(fun item->OciEvidence.shaFile(Path.Combine(selection.ManifestRoot,item.Path)));[|OciEvidence.shaFile(Path.Combine(selection.ManifestRoot,selection.NativeInventoryPath))|]]
        OciEvidence.shaBytes(Encoding.UTF8.GetBytes(String.concat "\n" hashes))
    let select selection trusted selectionPath trustedPath parent work evidence prepared custodyIdentity target program=
        if selection.IdentityClass<>"production" then fail "run-requires-production-identity"
        CommandRecipe.validate selection trusted prepared custodyIdentity target parent work evidence program
        requireRootlessNamespace()
        for path in [selectionPath;trustedPath;parent] do requirePrivateInput path
        privateDirectory work;privateDirectory evidence
        if work=evidence||work.StartsWith(evidence+"/",StringComparison.Ordinal)||evidence.StartsWith(work+"/",StringComparison.Ordinal) then fail "roots-overlap"
        {CustodyIdentity=custodyIdentity;Selection=selection;Trusted=trusted;SelectionPath=selectionPath;TrustedPath=trustedPath;ParentOci=parent;WorkRoot=work;EvidenceRoot=evidence;ExpectedInput=inputIdentity selection selectionPath trustedPath parent prepared custodyIdentity program;Prepared=prepared;Program=program}

/// One acknowledged bundle owns context, build store, reload store and all subprocess descendants.
type RootlessMechanism(inputs:RootlessInputs,backend:IProcessBackend)=
    let bundles=Dictionary<string,Bundle>()
    let mutable comparisonSealed=false
    let program=JsonSerializer.Deserialize<CommandProgram>(CommandRecipe.bytes inputs.Program)
    let mutable removals=0
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
        if name.Contains("-command-",StringComparison.Ordinal)||name.Contains("-output",StringComparison.Ordinal)||name.EndsWith("-probes.json",StringComparison.Ordinal) then backend.RetainCommandEvidence bytes
        if name="inactive-qualification.json" then
            use document=JsonDocument.Parse bytes
            let rec chargeRaw (node:JsonElement)=
                match node.ValueKind with
                | JsonValueKind.Object->
                    for item in node.EnumerateObject() do
                        if item.Name="managerStdout"||item.Name="hostStdout" then backend.RetainCommandEvidence(Encoding.UTF8.GetBytes(item.Value.GetRawText()))
                        else chargeRaw item.Value
                | JsonValueKind.Array->for item in node.EnumerateArray() do chargeRaw item
                | _->()
            chargeRaw document.RootElement
        use file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)
        file.Write bytes;file.Flush(true)
        File.SetUnixFileMode(path,UnixFileMode.UserRead)
    let validate (token:CancellationToken)=
        token.ThrowIfCancellationRequested()
        CommandRecipe.validate inputs.Selection inputs.Trusted inputs.Prepared inputs.CustodyIdentity program.Selection.Target inputs.ParentOci inputs.WorkRoot inputs.EvidenceRoot program
        match ImageClosure.prepareWithTrustedNative (Some inputs.Trusted) inputs.Selection with
        | Prepared bytes->require(bytes=inputs.Prepared) "prepared-input-changed"
        | _->raise(InvalidDataException "closed-input-no-longer-admitted")
        require(RootlessPolicy.inputIdentity inputs.Selection inputs.SelectionPath inputs.TrustedPath inputs.ParentOci inputs.Prepared inputs.CustodyIdentity program=inputs.ExpectedInput) "input-bytes-changed"
        token.ThrowIfCancellationRequested()
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
    let start (bundle:Bundle) (selectedCommand:ProcessCommand) (token:CancellationToken)=
        checkBuilder token
        require(selectedCommand.WorkingDirectory=bundle.Root&&selectedCommand.Environment=bundle.Environment) "recipe-bundle-command-join"
        let command={selectedCommand with Arguments=Array.copy selectedCommand.Arguments}
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
    let invokeCommand bundle command token=
        let child=start bundle command token
        let output=child.Wait token
        seal ($"{bundle.Logical}-output-{bundle.Children.Count}.json") output
        require(output.ExitCode=0) "podman-command-failed"
        require child.HasExited "process-not-quiescent"
        output.Stdout
    let invoke bundle step token=invokeCommand bundle (CommandRecipe.step program bundle.Logical step) token
    let inspect bundle step token=
        let raw=invoke bundle step token
        use doc=JsonDocument.Parse raw
        require(doc.RootElement.ValueKind=JsonValueKind.Array&&doc.RootElement.GetArrayLength()=1) "reload-image-ambiguous"
        let item=doc.RootElement[0]
        let result=bundle.Result.Value
        require(item.GetProperty("Digest").GetString()=result.ManifestDigest&&item.GetProperty("Id").GetString()=result.ConfigDigest&&item.GetProperty("Os").GetString()="linux"&&item.GetProperty("Architecture").GetString()="amd64") "reload-identity-changed"
    let export bundle label token=
        let archive=Path.Combine(inputs.EvidenceRoot,bundle.Logical+"-"+label+".oci.tar")
        invoke bundle (label+"-save") token|>ignore
        let result=OciEvidence.read token archive (Path.Combine(bundle.Root,"oci-"+label))
        OciEvidence.verifySelected inputs.Selection result
        File.SetUnixFileMode(archive,UnixFileMode.UserRead)
        result,archive
    let cleanup (bundle:Bundle) (token:CancellationToken)=
        require(lock bundle.Gate (fun()->not bundle.C5Active&&bundle.C5Retired&&not bundle.C5Residue)) "cleanup-c5-effect-not-retired"
        require(not bundle.UnacknowledgedChild) "cleanup-unacknowledged-child"
        require(bundle.Children.Values|>Seq.forall _.HasExited) "cleanup-outstanding-child"
        for reload in [false;true] do
            let prefix=if reload then "cleanup-reload-" else "cleanup-build-"
            let containers=invoke bundle (prefix+"census") token
            for id in containers.Split('\n',StringSplitOptions.RemoveEmptyEntries) do
                require(System.Text.RegularExpressions.Regex.IsMatch(id,"^[0-9a-f]{64}$")) "cleanup-container-identity"
                require(removals<program.Selection.CleanupRemovalLimit) "cleanup-removal-bound"
                removals<-removals+1
                invokeCommand bundle (CommandRecipe.removal program bundle.Logical reload id) token|>ignore
            require(String.IsNullOrWhiteSpace(invoke bundle (prefix+"empty") token)) "cleanup-containers-remain"
            let mounts=invoke bundle (prefix+"mount") token
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
        require((Runner.c4Ready state||Runner.qualificationAccepted state)&&comparisonSealed) "terminal-not-qualified"
        let accepted=Runner.qualificationAccepted state
        seal "terminal.json" {|schema=(if accepted then "fsgg.telemetry.persistent-v3-inactive-result/1" else "fsgg.telemetry.persistent-v3-c4-result/1");status=(if accepted then "InactiveQualified" else "C4Ready");input=inputs.ExpectedInput;custody=inputs.CustodyIdentity;qualificationAccepted=accepted;cleanupObserved=state.Owned.IsEmpty&&state.Running.IsEmpty;effects=trace|>List.map(fun(effect,observation)->sprintf "%A -> %A" effect observation)|}
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
                seal "input.json" {|schema="fsgg.telemetry.persistent-v3-c4-input/1";input=expected;custody=inputs.CustodyIdentity;parent=parent.ManifestDigest;builder=RootlessPolicy.BuilderSha256;supervisor=RootlessPolicy.SupervisorSha256;commandPolicy=RootlessPolicy.commandPolicy|}
                InputsAcquired expected
            | ValidateInputs expected->validate token;InputsValidated inputs.ExpectedInput
            | CreateStore logical->
                require((logical="a"||logical="b")&&not(bundles.ContainsKey logical)) "store-logical"
                let root=Path.Combine(inputs.WorkRoot,"bundle-"+logical)
                require(not(Directory.Exists root)) "store-not-fresh"
                Directory.CreateDirectory(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
                let nonce=CommandRecipe.nonce program logical
                File.WriteAllText(Path.Combine(root,"owner"),nonce)
                let directories=[|"build-graph";"build-run";"build-tmp";"reload-graph";"reload-run";"reload-tmp";"context";"runtime";"tmp"|]
                for name in directories do token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.Combine(root,name))|>ignore
                File.WriteAllText(Path.Combine(root,"auth.json"),"{\"auths\":{}}")
                File.WriteAllText(Path.Combine(root,"containers.conf"),"[engine]\nevents_logger=\"none\"\n")
                File.WriteAllText(Path.Combine(root,"storage.conf"),$"[storage]\ndriver=\"vfs\"\ngraphroot=\"{root}/build-graph\"\nrunroot=\"{root}/build-run\"\n")
                let env=CommandRecipe.environment root
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
                let bundle={Logical=logical;Root=root;Nonce=nonce;Environment=env;Contexts=[|context|];Children=Dictionary();Build=None;Result=None;UnacknowledgedChild=false;Gate=obj();C5Active=false;C5Retired=true;C5Residue=false}
                bundles.Add(logical,bundle)
                StoreCreated(logical,root+"#"+nonce)
            | StartBuild(logical,expected)->
                validate token
                require(expected=inputs.ExpectedInput&&(bundles.Values|>Seq.forall(fun b->b.Children.Values|>Seq.forall _.HasExited))) "build-concurrency-or-input"
                let bundle=bundles[logical]
                validateContext bundle token
                invoke bundle "parent-load" token|>ignore
                let inspectRaw=invoke bundle "parent-inspect" token
                use parentDoc=JsonDocument.Parse inspectRaw
                require(parentDoc.RootElement.GetArrayLength()=1&&parentDoc.RootElement[0].GetProperty("Digest").GetString()=inputs.Selection.RuntimeImageDigest) "loaded-parent-identity"
                let child=start bundle (CommandRecipe.step program logical "build") token
                bundle.Build<-Some child
                BuildStarted(logical,child.Identity)
            | AwaitBuild(logical,identity,expected)->
                let bundle=bundles[logical]
                let child=bundle.Build.Value
                require(child.Identity=identity&&expected=inputs.ExpectedInput) "build-process-stale"
                let output=child.Wait token
                seal (logical+"-build-output.json") output
                require(output.ExitCode=0&&child.HasExited) "build-exit-not-success"
                let result,_=export bundle "build" token
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
                        invoke bundle "reload-load" token|>ignore
                        inspect bundle "reload-inspect" token
                        let reloaded,_=export bundle "reload" token
                        require(OciEvidence.equivalent bundle.Result.Value reloaded) "reload-evidence-changed"
                        let dotnet=invoke bundle "probe-dotnet" token
                        require(dotnet.Contains("Microsoft.NETCore.App 10.0.12")&&dotnet.Contains("Microsoft.AspNetCore.App 10.0.12")) "runtime-probe-version"
                        let python=invoke bundle "probe-python" token
                        require(python.Trim()="Python 3.14.0") "python-probe-version"
                        let native=invoke bundle "probe-native" token
                        require(native.Trim()="codex-cli 0.158.0") "native-probe-version"
                        seal (bundle.Logical+"-probes.json") {|dotnet=dotnet;python=python;native=native|}
                    token.ThrowIfCancellationRequested()
                    seal "comparison.json" {|schema="fsgg.telemetry.persistent-v3-c4-comparison/1";input=inputs.ExpectedInput;first=first.Result.Value;second=second.Result.Value;ociEqual=true;rawArchivesEqual=first.Result.Value.ArchiveSha256=second.Result.Value.ArchiveSha256;c5Qualification="unknown"|}
                    comparisonSealed<-true
                    BuildsCompared true
            | QualifyInactive digest->
                require comparisonSealed "inactive-comparison-missing"
                validate token
                let owned=[|bundles["a"];bundles["b"]|]
                for bundle in owned do require(bundle.Result.Value.ManifestDigest=digest) "inactive-image-digest-stale"
                for bundle in owned do
                    lock bundle.Gate (fun()->require(not bundle.C5Active&&bundle.C5Retired&&not bundle.C5Residue) "inactive-effect-not-fresh";bundle.C5Active<-true;bundle.C5Retired<-false)
                let mutable firstFailure:string option=None
                let cleanupFailures=ResizeArray<string>()
                let results=ResizeArray<_>()
                try
                    for bundle in owned do
                        token.ThrowIfCancellationRequested()
                        inspect bundle "inactive-inspect" token
                        let mutable inactiveOrdinal=0
                        let result=InactiveQualification.run inputs.Selection inputs.Trusted bundle.Root (fun arguments cancellation->
                            let step=match inactiveOrdinal with 0->"inactive-manager"|1->"inactive-host"|_->raise(InvalidDataException "inactive-command-count")
                            inactiveOrdinal<-inactiveOrdinal+1
                            let selected=CommandRecipe.step program bundle.Logical step
                            require(selected.Arguments.Length>=arguments.Length&&selected.Arguments[selected.Arguments.Length-arguments.Length..]=arguments) "inactive-recipe-command-drift"
                            invoke bundle step cancellation) token
                        require(inactiveOrdinal=2) "inactive-command-count"
                        results.Add(bundle.Logical,result)
                    validate token
                    token.ThrowIfCancellationRequested()
                with error->firstFailure<-Some error.Message
                // A timed-out Execute can finish late. Retirement remains false until all owned
                // groups drain and fixtures are absent; RemoveStore refuses while this is active.
                for bundle in owned do
                    let mutable retired=false
                    try
                        use cleanupToken=new CancellationTokenSource(TimeSpan.FromSeconds 30.)
                        require(not bundle.UnacknowledgedChild) "inactive-unacknowledged-child"
                        for child in bundle.Children.Values do
                            if not child.HasExited then child.Cancel cleanupToken.Token
                            require child.HasExited "inactive-child-retirement-unknown"
                        InactiveQualification.remove bundle.Root
                        retired<-true
                    with error->cleanupFailures.Add error.Message
                    lock bundle.Gate (fun()->bundle.C5Retired<-retired;bundle.C5Residue<-not retired;bundle.C5Active<-false)
                if firstFailure.IsSome||cleanupFailures.Count>0||token.IsCancellationRequested then
                    let reason=firstFailure|>Option.defaultValue(if token.IsCancellationRequested then "inactive-cancelled" else "inactive-cleanup-unknown")
                    seal "inactive-failure.json" {|input=inputs.ExpectedInput;firstFailure=reason;cleanupFailures=cleanupFailures.ToArray();qualification="unknown";retired=owned|>Array.map(fun bundle->bundle.Logical,bundle.C5Retired)|}
                    EffectFailed reason
                else
                    validate token
                    token.ThrowIfCancellationRequested()
                    require(owned|>Array.forall(fun bundle->bundle.C5Retired&&not bundle.C5Residue)) "inactive-retirement-unobserved"
                    seal "inactive-qualification.json" {|schema="fsgg.telemetry.served-inactive-qualification/1";input=inputs.ExpectedInput;image=digest;servedHostArchiveSha256=inputs.Selection.HostArchiveSha256;servedManagerArchiveSha256=inputs.Selection.ManagerArchiveSha256;results=results.ToArray();fixtureCleanupObserved=true;nativeAcceptanceClaimed=false;activationAuthorized=false|}
                    token.ThrowIfCancellationRequested()
                    QualificationObserved(true,inputs.ExpectedInput)
            | RemoveStore identity->
                let bundle=bundles.Values|>Seq.find(fun b->b.Root+"#"+b.Nonce=identity)
                cleanup bundle token
                StoreAbsent identity),token)
