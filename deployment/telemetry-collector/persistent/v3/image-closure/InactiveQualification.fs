namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Text.Encodings.Web
open System.Xml
open System.Xml.Linq
open System.Threading
open Microsoft.Win32.SafeHandles
open FSGG.Telemetry.PersistentV3

module InactiveQualification =
    [<Literal>]
    let Policy="inactive-preparation/1;fixture/1;uid32768;network-none;readonly;manager-rw;host-ro;no-native-launch;output1MiB;retirement30s"
    [<DllImport("libc",SetLastError=true)>]
    extern int chown(string path,uint32 owner,uint32 group)
    let private require condition reason=if not condition then raise(InvalidDataException reason)
    [<DllImport("libc",EntryPoint="open",SetLastError=true)>]
    extern int openFile(string path,int flags)
    [<DllImport("libc",SetLastError=true)>]
    extern int fstat(int descriptor,byte[] bytes)
    let private metadata path directory=
        let fd=openFile(path,0x80000|||0x20000|||0x200000)
        require(fd>=0) "inactive-census-descriptor"
        use handle=new SafeFileHandle(nativeint fd,true)
        let data=Array.zeroCreate<byte> 144
        require(fstat(fd,data)=0) "inactive-census-stat"
        let mode=BitConverter.ToUInt32(data,24)
        require((mode&&&0xf000u)=(if directory then 0x4000u else 0x8000u)) "inactive-census-type"
        require(directory||BitConverter.ToUInt64(data,16)=1UL) "inactive-census-links"
        BitConverter.ToUInt32(data,28),mode&&&0xfffu,BitConverter.ToUInt64(data,8)
    let private json value=JsonSerializer.SerializeToUtf8Bytes value
    let private privateMode=UnixFileMode.UserRead|||UnixFileMode.UserWrite
    let private own path mode=
        require(chown(path,32768u,32768u)=0) "inactive-fixture-owner"
        File.SetUnixFileMode(path,mode)
    let private write path bytes=
        use stream=new FileStream(path,FileStreamOptions(Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,UnixCreateMode=privateMode))
        stream.Write(bytes:byte array);stream.Flush true
        own path privateMode
    let private create path=
        require(not(Directory.Exists path||File.Exists path)) "inactive-root-not-fresh"
        Directory.CreateDirectory(path,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
        own path (UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
    let private selected (selection:Selection) target=
        let rows=selection.Files|>Array.filter(fun r->r.TargetPath=target)
        require(rows.Length=1) "inactive-selected-file-ambiguous"
        rows[0]
    let private readSelected (selection:Selection) (row:FileRow) maximum=
        let path=Path.Combine(selection.AcquisitionRoot,row.SourcePath)
        require(row.Bytes>0L&&row.Bytes<=maximum&&String.IsNullOrEmpty(FileInfo(path).LinkTarget)) "inactive-selected-file-custody"
        let _,_,inode=metadata path false
        use input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)
        use collected=new MemoryStream()
        let chunk=Array.zeroCreate<byte> 65536
        let mutable read=input.Read(chunk,0,chunk.Length)
        while read>0 do
            require(collected.Length+int64 read<=row.Bytes) "inactive-selected-file-size-drift"
            collected.Write(chunk,0,read);read<-input.Read(chunk,0,chunk.Length)
        let _,_,after=metadata path false
        require(inode=after) "inactive-selected-file-identity-drift"
        let bytes=collected.ToArray()
        require(int64 bytes.Length=row.Bytes&&OciEvidence.shaBytes bytes=row.Sha256) "inactive-selected-file-drift"
        bytes
    // Exact release-saga payload normalization: all producer entries except signature/core
    // properties; OPC relationships drop only the excluded core-properties relationship.
    let private normalized (name:string) (raw:byte array)=
        let lower=name.ToLowerInvariant()
        if lower.EndsWith(".rels")&&(lower.StartsWith("_rels/")||lower.Contains("/_rels/")) then
            try
                use input=new MemoryStream(raw,false)
                use xml=XmlReader.Create(input,XmlReaderSettings(DtdProcessing=DtdProcessing.Prohibit))
                let document=XDocument.Load xml
                use output=new MemoryStream()
                use writer=new Utf8JsonWriter(output,JsonWriterOptions(Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
                writer.WriteStartArray()
                for element in document.Root.Elements() do
                    let target=element.Attribute(XName.Get "Target")
                    if not(element.Name.LocalName="Relationship"&&not(isNull target)&&target.Value.EndsWith(".psmdcp",StringComparison.OrdinalIgnoreCase)) then
                        writer.WriteStartArray();writer.WriteStringValue element.Name.LocalName;writer.WriteStartObject()
                        for attribute in element.Attributes()|>Seq.filter(fun a->not a.IsNamespaceDeclaration)|>Seq.sortBy(fun a->a.Name.ToString()) do writer.WriteString(attribute.Name.ToString(),attribute.Value)
                        writer.WriteEndObject();writer.WriteEndArray()
                writer.WriteEndArray();writer.Flush();output.ToArray()
            with :? XmlException->raw
        else raw
    let private package (selection:Selection) staging (token:CancellationToken)=
        require(selection.HostArchiveSha256<>"c7cbaa474fdcd0ba577f8577ec92bb381f032db7842f86ddb1b3fb050d8a97ad") "inactive-successor-served-host-required"
        let rows=selection.Files|>Array.filter(fun row->row.SourceClass="host")
        require(rows.Length=1&&rows[0].Sha256=selection.HostArchiveSha256) "inactive-host-package-binding"
        let bytes=readSelected selection rows[0] (64L*1024L*1024L)
        create staging
        use memory=new MemoryStream(bytes,false)
        use archive=new ZipArchive(memory,ZipArchiveMode.Read)
        require(archive.Entries.Count>0&&archive.Entries.Count<=8192) "inactive-package-count"
        let seen=System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        let inventory=ResizeArray<string*int64*string>()
        let producer=System.Collections.Generic.SortedDictionary<string,string>(StringComparer.Ordinal)
        let mutable total=0L
        for entry in archive.Entries do
            token.ThrowIfCancellationRequested()
            let name=entry.FullName
            let segments=name.TrimEnd('/').Split('/')
            require(not(String.IsNullOrEmpty name)&&not(name.StartsWith("/"))&&not(name.Contains("\\"))&&(segments|>Array.forall(fun s->s<>""&&s<>"."&&s<>".."))&&seen.Add name) "inactive-package-path"
            let fileType=(uint32 entry.ExternalAttributes>>>16)&&&0xf000u
            require(fileType=0u||fileType=0x8000u||fileType=0x4000u) "inactive-package-link"
            require(entry.Length>=0L&&entry.Length<=64L*1024L*1024L&&total<=256L*1024L*1024L-entry.Length) "inactive-package-size"
            total<-total+entry.Length
            if not(name.EndsWith("/")) then
                use source=entry.Open()
                use copy=new MemoryStream()
                let chunk=Array.zeroCreate<byte> 65536
                let mutable read=source.Read(chunk,0,chunk.Length)
                while read>0 do
                    token.ThrowIfCancellationRequested()
                    require(copy.Length+int64 read<=entry.Length) "inactive-package-length"
                    copy.Write(chunk,0,read);read<-source.Read(chunk,0,chunk.Length)
                require(copy.Length=entry.Length) "inactive-package-truncated"
                if not(name.Equals(".signature.p7s",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".psmdcp",StringComparison.OrdinalIgnoreCase)) then producer.Add(name,OciEvidence.shaBytes(normalized name (copy.ToArray())))
            if name.StartsWith("tools/net10.0/any/",StringComparison.Ordinal)&&not(name.EndsWith("/")) then
                let relative=name.Substring("tools/net10.0/any/".Length)
                require(relative<>""&&fileType<>0x4000u) "inactive-package-payload"
                let destination=Path.Combine(staging,relative)
                Directory.CreateDirectory(Path.GetDirectoryName destination)|>ignore
                use input=entry.Open()
                use output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None)
                let buffer=Array.zeroCreate<byte> 65536
                let mutable copied=0L
                let mutable count=input.Read(buffer,0,buffer.Length)
                while count>0 do
                    token.ThrowIfCancellationRequested();copied<-copied+int64 count
                    require(copied<=entry.Length) "inactive-package-length"
                    output.Write(buffer,0,count);count<-input.Read(buffer,0,buffer.Length)
                output.Flush true;require(copied=entry.Length) "inactive-package-truncated"
                output.Dispose()
                inventory.Add(relative,copied,OciEvidence.shaFile destination)
                own destination UnixFileMode.UserRead
        use payloadBytes=new MemoryStream()
        use payloadWriter=new Utf8JsonWriter(payloadBytes,JsonWriterOptions(Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping))
        payloadWriter.WriteStartObject()
        for KeyValue(name,digest) in producer do payloadWriter.WriteString(name,digest)
        payloadWriter.WriteEndObject();payloadWriter.Flush()
        require(OciEvidence.shaBytes(payloadBytes.ToArray())=selection.HostPayloadSha256) "inactive-host-producer-payload-drift"
        require(inventory.Count>0&&File.Exists(Path.Combine(staging,"FS.GG.Telemetry.Host.dll"))) "inactive-host-entrypoint-missing"
        for directory in Directory.EnumerateDirectories(staging,"*",SearchOption.AllDirectories) do own directory (UnixFileMode.UserRead|||UnixFileMode.UserExecute)
        own staging (UnixFileMode.UserRead|||UnixFileMode.UserExecute)
        inventory.ToArray()
    let private census root=
        let rows=ResizeArray<string*string*int64*string>()
        let rec visit path=
            let uid,mode,_=metadata path true
            require(String.IsNullOrEmpty(DirectoryInfo(path).LinkTarget)&&uid=32768u&&mode=0x1c0u) "inactive-fixture-directory-drift"
            for item in DirectoryInfo(path).EnumerateFileSystemInfos() do
                require(String.IsNullOrEmpty item.LinkTarget) "inactive-fixture-link"
                if item.Attributes.HasFlag FileAttributes.Directory then
                    rows.Add(Path.GetRelativePath(root,item.FullName),"directory",int64(File.GetUnixFileMode item.FullName),"");visit item.FullName
                else
                    let uid,mode,_=metadata item.FullName false
                    require(uid=32768u&&mode=0x180u&&FileInfo(item.FullName).Length<=1024L*1024L) "inactive-fixture-file-drift"
                    rows.Add(Path.GetRelativePath(root,item.FullName),Convert.ToString(int(File.GetUnixFileMode item.FullName),8),FileInfo(item.FullName).Length,OciEvidence.shaFile item.FullName)
        visit root
        require(rows.Count<=32) "inactive-fixture-census-limit"
        rows.ToArray()|>Array.sort
    /// Checks the closed Host result against the actual retained fixture and Manager output.
    let validateResult (expected:Map<string,string>) (stdout:string)=
        require(stdout.StartsWith("{",StringComparison.Ordinal)&&stdout.EndsWith("}\n",StringComparison.Ordinal)&&not(stdout.Substring(0,stdout.Length-1).Contains("\n"))) "inactive-result-framing"
        let hashNames=set["hostConfigSha256";"managerReceiptSha256";"sidecarSha256";"executableSha256";"sourceReferenceSha256";"readerProfileSha256";"verifierRuntimeManifestSha256"]
        require((expected|>Map.toSeq|>Seq.map fst|>Set.ofSeq)=hashNames) "inactive-expected-hashes-not-closed"
        use document=JsonDocument.Parse stdout
        let r=document.RootElement
        let fields=["schema";"status";"hostConfigSha256";"managerReceiptSha256";"sidecarSha256";"executableSha256";"sourceReferenceSha256";"readerProfileSha256";"verifierRuntimeManifestSha256";"sourceVerification";"snapshotOrigin";"sharedCostCompleteness";"captureQualified";"nativeAcceptanceClaimed";"activationAuthorized"]
        let names=r.EnumerateObject()|>Seq.map _.Name|>Seq.toArray
        require(names.Length=fields.Length&&Set.ofArray names=Set.ofList fields) "inactive-result-not-closed"
        require(r.GetProperty("schema").GetString()="fsgg.telemetry.inactive-preparation-read/1"&&r.GetProperty("status").GetString()="prepared-inactive") "inactive-result-status"
        for KeyValue(name,digest) in expected do require(r.GetProperty(name).GetString()=digest) "inactive-result-hash-drift"
        for name in ["sourceVerification";"snapshotOrigin";"sharedCostCompleteness"] do require(r.GetProperty(name).GetString()="unknown") "inactive-result-claim"
        for name in ["captureQualified";"nativeAcceptanceClaimed";"activationAuthorized"] do require(not(r.GetProperty(name).GetBoolean())) "inactive-result-activation"
    /// Prepares and runs one served Manager/Host pair; the owner retires children before removal.
    let run (selection:Selection) (trusted:TrustedNativeSelection) root (invoke:string array->CancellationToken->string) (token:CancellationToken)=
        let fixture=Path.Combine(root,"inactive-fixture")
        let staging=Path.Combine(root,"inactive-host")
        create fixture
        for name in ["codex-home";"evidence"] do create(Path.Combine(fixture,name))
        write (Path.Combine(fixture,"placeholder")) (Encoding.UTF8.GetBytes "inactive-fixture-not-a-credential\n")
        let config=json {|Schema="fsgg.telemetry.host-config/2";Credentials=[|{|Reference="inactive-fixture";SecretFile="/qualification/placeholder";WorkspaceId="inactive-workspace";ProducerId="inactive-producer";StreamId="inactive-stream";Role="native-collector";GrantId="inactive-fixture-grant";GrantGeneration=1L;Revoked=false|}|]|}
        write (Path.Combine(fixture,"host.json")) config
        let profile=readSelected selection (selected selection "/opt/fsgg/profile/reader-profile.json") (1024L*1024L)
        require(OciEvidence.shaBytes profile=selection.ReaderProfileSha256) "inactive-profile-drift"
        write (Path.Combine(fixture,"evidence/fixed-native-capability-profile.json")) profile
        let native=selected selection "/opt/fsgg/codex/codex"
        require(native.Sha256=trusted.NativeExecutableSha256) "inactive-native-selection-drift"
        let python=selected selection trusted.PythonExecutable
        let moduleRow=selected selection "/opt/fsgg/verifier/learn_01_native_source.py"
        let files=selection.Files|>Array.filter(fun row->row.SourceClass="native")|>Array.sortBy _.TargetPath|>Array.map(fun row->{|path=row.TargetPath;bytes=row.Bytes;sha256=row.Sha256|})
        let runtimeManifest=json {|schema="fsgg.telemetry.native-verifier-runtime/1";sourceRevision=selection.HostSourceRevision;runtimeImageDigest=selection.RuntimeImageDigest;runtimeExecutablePath=python.TargetPath;modulePath=moduleRow.TargetPath;files=files|}
        let manifestHash=OciEvidence.shaBytes runtimeManifest
        // The preparation is explicitly synthetic; its artifact provenance is never acceptance evidence.
        let input=json {|schema="fsgg.telemetry.persistent-v3-preparation-input/1";identityClass="synthetic-test";
                        manager={|sourceRevision=selection.ManagerSourceRevision;sourceTree=selection.ManagerSourceTree;artifactUri="https://example.invalid/inactive-manager-fixture";artifactSha256=selection.ManagerArtifactSha256;manifestSha256=selection.ManagerManifestSha256|};
                        host={|sourceRevision=selection.HostSourceRevision;sourceTree=String('1',40);artifactUri="https://example.invalid/inactive-host-fixture";artifactSha256=selection.HostArchiveSha256;manifestSha256=selection.HostManifestSha256;journalSha256=selection.HostJournalSha256|};
                        installation={|schema="fsgg.telemetry.native-collector-installation/3";hostConfigPath="/qualification/host.json";credentialReference="inactive-fixture";executablePath=native.TargetPath;executableSha256=native.Sha256;codexHome="/qualification/codex-home";evidenceRoot="/qualification/evidence";provider="openai";model="gpt-5.6-sol";effort="medium"|};
                        sourceGrant={|credentialReference="inactive-fixture";workspaceId="inactive-workspace";producerId="inactive-producer";streamId="inactive-stream";role="native-collector";grantId="inactive-fixture-grant";grantGeneration=1L|};
                        sourceReference={|schema="fsgg.telemetry.persistent-source-references/3";profileSha256=selection.NativeProfileSha256;nativeSourceVolume="inactive-fixture-source";developmentTarget="/qualification/codex-home";collectorReadOnlyTarget="/qualification/codex-home";readerProfileSha256=selection.ReaderProfileSha256;captureQualified=false;verifierRuntimeManifestSha256=manifestHash|};
                        verifierRuntime={|schema="fsgg.telemetry.native-verifier-runtime/1";sourceRevision=selection.HostSourceRevision;runtimeImageDigest=selection.RuntimeImageDigest;runtimeExecutablePath=python.TargetPath;runtimeExecutableSha256=python.Sha256;modulePath=moduleRow.TargetPath;moduleSha256=moduleRow.Sha256;manifestPath="/qualification/verifier-runtime.json";manifestSha256=manifestHash;files=files|};
                        recovery={|coverage=[|"configuration";"credentials";"evidence";"manager-receipt";"manager-sidecar";"quiesced-store";"retained-native-source";"source-references";"verifier-runtime"|];requiredPrivateInputs=[|"credential-secret";"evidence";"host-configuration";"persistent-store";"retained-native-source"|]|}|}
        let prepared=match Preparation.prepare input with Ok bytes->bytes|Error reason->raise(InvalidDataException reason)
        use preparation=JsonDocument.Parse prepared
        let p=preparation.RootElement
        write (Path.Combine(fixture,"source-reference.json")) (Convert.FromBase64String(p.GetProperty("sourceReferenceBytesBase64").GetString()))
        write (Path.Combine(fixture,"verifier-runtime.json")) (Convert.FromBase64String(p.GetProperty("verifierRuntimeManifestBytesBase64").GetString()))
        let payload=package selection staging token
        let baseArgs readonlyFixture entrypoint=
            [|"run";"--rm";"--network";"none";"--read-only";"--cap-drop";"ALL";"--security-opt";"no-new-privileges";"--cpus";"1";"--memory";"512m";"--pids-limit";"64";"--user";"32768:32768";"--tmpfs";"/tmp:rw,noexec,nosuid,nodev,size=64m,mode=1777";"--env";"DOTNET_EnableDiagnostics=0";"--env";"DOTNET_ROLL_FORWARD=Disable";"--env";"HOME=/qualification/codex-home";"--volume";fixture+":/qualification:"+(if readonlyFixture then "ro" else "rw");"--volume";staging+":/qualification-host:ro";"--entrypoint";entrypoint;"localhost/learn-p2c4:inert"|]
        let manager=[|"exec";"--fx-version";"10.0.12";"/opt/fsgg/telemetry-host-manager/TelemetryHostManager.dll"|]
        let arguments=p.GetProperty("managerArguments").EnumerateArray()|>Seq.map _.GetString()|>Seq.toArray
        let managerOutput=invoke (Array.concat[baseArgs false "/usr/share/dotnet/dotnet";manager;arguments]) token
        token.ThrowIfCancellationRequested()
        let receiptPath=Path.Combine(fixture,"host.json.native-collector.receipt.json")
        require(FileInfo(receiptPath).Length<=65536L) "inactive-manager-receipt-size"
        let receipt=File.ReadAllBytes receiptPath
        require(managerOutput=Encoding.UTF8.GetString(receipt)+"\n") "inactive-manager-stdout-receipt"
        let before=census fixture
        let expectedPaths=set["codex-home";"evidence";"evidence/fixed-native-capability-profile.json";"placeholder";"host.json";"host.json.native-collector.json";"host.json.native-collector.receipt.json";"source-reference.json";"verifier-runtime.json"]
        require((before|>Array.map(fun(path,_,_,_)->path)|>Set.ofArray)=expectedPaths) "inactive-manager-unexpected-file"
        let hostOutput=invoke (Array.concat[baseArgs true "/usr/share/dotnet/dotnet";[|"exec";"--fx-version";"10.0.12";"/qualification-host/FS.GG.Telemetry.Host.dll";"read-inactive-preparation";"--config";"/qualification/host.json"|]]) token
        token.ThrowIfCancellationRequested()
        require(census fixture=before) "inactive-host-mutated-fixture"
        let expected=Map.ofList["hostConfigSha256",OciEvidence.shaBytes config;"managerReceiptSha256",OciEvidence.shaBytes receipt;"sidecarSha256",OciEvidence.shaFile(Path.Combine(fixture,"host.json.native-collector.json"));"executableSha256",native.Sha256;"sourceReferenceSha256",OciEvidence.shaFile(Path.Combine(fixture,"source-reference.json"));"readerProfileSha256",selection.ReaderProfileSha256;"verifierRuntimeManifestSha256",manifestHash]
        validateResult expected hostOutput
        {|managerStdout=managerOutput;hostStdout=hostOutput;expected=expected;fixtureCensus=before;hostPayload=payload;policy=Policy;nativeAcceptanceClaimed=false;activationAuthorized=false|}
    /// Called only after every owned process has acknowledged exit and drain.
    let remove root=
        for name in ["inactive-fixture";"inactive-host"] do
            let path=Path.Combine(root,name)
            if Directory.Exists path then
                let rec admit directory=
                    require(String.IsNullOrEmpty(DirectoryInfo(directory).LinkTarget)) "inactive-cleanup-link"
                    let uid,_,_=metadata directory true
                    require(uid=32768u) "inactive-cleanup-owner"
                    for entry in DirectoryInfo(directory).EnumerateFileSystemInfos() do
                        require(String.IsNullOrEmpty entry.LinkTarget) "inactive-cleanup-link"
                        if entry.Attributes.HasFlag FileAttributes.Directory then admit entry.FullName
                        else metadata entry.FullName false|>ignore
                    File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
                admit path
                Directory.Delete(path,true)
            require(not(Directory.Exists path||File.Exists path)) "inactive-cleanup-unacknowledged"
