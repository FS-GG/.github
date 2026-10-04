namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open Microsoft.Win32.SafeHandles

module InactivePreparation =
    module private Native =
        [<DllImport("libc",EntryPoint="open",SetLastError=true)>]
        extern int openFile(string path,int flags)
        [<DllImport("libc",SetLastError=true)>]
        extern int openat(int directory,string path,int flags)
        [<DllImport("libc",SetLastError=true)>]
        extern int fstat(int descriptor, byte[] buffer)
        [<DllImport("libc")>]
        extern uint32 geteuid()
    type private Stamp={Device:uint64;Inode:uint64;Links:uint64;Mode:uint32;Uid:uint32;Length:int64;Modified:int64*int64;Changed:int64*int64}
    let private require condition=if not condition then raise(InvalidDataException "inactive-preparation-unavailable")
    let private stamp (handle:SafeFileHandle)=
        let bytes=Array.zeroCreate<byte> 144
        require(Native.fstat(handle.DangerousGetHandle().ToInt32(),bytes)=0)
        {Device=BitConverter.ToUInt64(bytes,0);Inode=BitConverter.ToUInt64(bytes,8);Links=BitConverter.ToUInt64(bytes,16);Mode=BitConverter.ToUInt32(bytes,24);Uid=BitConverter.ToUInt32(bytes,28);Length=BitConverter.ToInt64(bytes,48);Modified=(BitConverter.ToInt64(bytes,88),BitConverter.ToInt64(bytes,96));Changed=(BitConverter.ToInt64(bytes,104),BitConverter.ToInt64(bytes,112))}
    let private canonical (value:string)=
        not(String.IsNullOrWhiteSpace value)&&Encoding.UTF8.GetByteCount(value)<=4096&&
        Regex.IsMatch(value,@"\A/[\p{L}\p{M}0-9._+=-]+(?:/[\p{L}\p{M}0-9._+=-]+)*\z")&&
        (value.Substring(1).Split('/')|>Array.forall(fun s->s<>"."&&s<>".."&&Encoding.UTF8.GetByteCount(s)<=255))
    let private descriptor path directory=
        require(canonical path)
        // Every component is opened relative to an already-held directory descriptor.
        let flags=0x80000|||0x20000|||0x800
        let root=Native.openFile("/",flags|||0x10000)
        require(root>=0)
        let mutable held=new SafeFileHandle(nativeint root,true)
        try
            let parts=path.Substring(1).Split('/')
            for index in 0..parts.Length-1 do
                let isDirectory=directory||index<parts.Length-1
                let fd=Native.openat(held.DangerousGetHandle().ToInt32(),parts[index],flags|||(if isDirectory then 0x10000 else 0))
                require(fd>=0)
                let next=new SafeFileHandle(nativeint fd,true)
                held.Dispose();held<-next
                let observed=stamp held
                require((observed.Mode&&&0xf000u)=(if isDirectory then 0x4000u else 0x8000u))
            held
        with _->held.Dispose();reraise()
    let private sha bytes=Convert.ToHexString(SHA256.HashData(bytes:byte array)).ToLowerInvariant()
    let private hash value=not(isNull value)&&Regex.IsMatch(value,@"\A[0-9a-f]{64}\z")&&value<>String('0',64)
    let private exact names (value:JsonElement)=
        require(value.ValueKind=JsonValueKind.Object)
        let properties=value.EnumerateObject()|>Seq.map _.Name|>Seq.toArray
        require(properties.Length=List.length names&&Set.ofArray properties=Set.ofList names)
    let rec private duplicates (value:JsonElement)=
        match value.ValueKind with
        | JsonValueKind.Object->
            let rows=value.EnumerateObject()|>Seq.toArray
            require(rows.Length=(rows|>Array.map _.Name|>Array.distinct).Length)
            for row in rows do duplicates row.Value
        | JsonValueKind.Array->for row in value.EnumerateArray() do duplicates row
        | _->()
    let private text (name:string) (value:JsonElement)=
        let result=value.GetProperty name
        require(result.ValueKind=JsonValueKind.String)
        let s=result.GetString()
        require(not(String.IsNullOrWhiteSpace s));s
    let private number (name:string) (value:JsonElement)=value.GetProperty(name).GetInt64()
    let private boolean (name:string) (value:JsonElement)=value.GetProperty(name).GetBoolean()
    let private id (value:string)=Regex.IsMatch(value,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z")
    let private failure=Encoding.UTF8.GetBytes("{\"schema\":\"fsgg.telemetry.inactive-preparation-read/1\",\"status\":\"unavailable\",\"code\":\"inactive-preparation-unavailable\"}\n")
    let read configPath=
        try
            require(OperatingSystem.IsLinux()&&RuntimeInformation.ProcessArchitecture=Architecture.X64)
            let uid=Native.geteuid()
            let bound=ResizeArray<string*Stamp*string>()
            let directories=ResizeArray<string*Stamp>()
            let directory path privateRequired=
                use handle=descriptor path true
                let value=stamp handle
                require(value.Uid=uid&&(not privateRequired||(value.Mode&&&0xfffu)=0x1c0u))
                directories.Add(path,value)
            let inspect path privateRequired executable maximum collect=
                use handle=descriptor path false
                let before=stamp handle
                require(before.Links=1UL&&before.Length>=0L&&before.Length<=maximum)
                require((if privateRequired then before.Uid=uid&&(before.Mode&&&0xfffu)=0x180u else (before.Uid=uid||before.Uid=0u)&&(before.Mode&&&0x12u)=0u))
                require(not executable||(before.Mode&&&0x49u)<>0u)
                use stream=new FileStream(handle,FileAccess.Read)
                use hasher=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                use collected=new MemoryStream()
                let buffer=Array.zeroCreate<byte> 65536
                let mutable total=0L
                let mutable count=stream.Read(buffer,0,buffer.Length)
                while count>0 do
                    total<-total+int64 count;require(total<=maximum&&total<=before.Length)
                    hasher.AppendData(buffer,0,count)
                    if collect then collected.Write(buffer,0,count)
                    count<-stream.Read(buffer,0,buffer.Length)
                require(total=before.Length&&stamp handle=before)
                let digest=Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()
                use finalHandle=descriptor path false
                require(stamp finalHandle=before)
                bound.Add(path,before,digest)
                before,digest,collected.ToArray()
            let json path maximum=
                let _,digest,bytes=inspect path true false maximum true
                require(bytes.Length>0)
                let doc=JsonDocument.Parse(bytes,JsonDocumentOptions(MaxDepth=32))
                try duplicates doc.RootElement;doc,digest
                with _->doc.Dispose();reraise()
            require(canonical configPath)
            let anchor=Path.GetDirectoryName configPath
            directory anchor true
            let configDocument,configHash=json configPath (1024L*1024L)
            use config=configDocument
            let c=config.RootElement
            let minimal=["Schema";"Credentials"]
            let full=["Schema";"ListenUrl";"CertificatePath";"CertificatePasswordFile";"ServiceLockPath";"Stores";"Credentials";"BrowserPrincipals";"BrowserSession"]
            let fields=c.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq
            exact (if fields=Set.ofList minimal then minimal else full) c
            require(text "Schema" c="fsgg.telemetry.host-config/2")
            if fields=Set.ofList full then
                for name in ["ListenUrl";"CertificatePath";"CertificatePasswordFile";"ServiceLockPath"] do text name c|>ignore
                for item in c.GetProperty("Stores").EnumerateArray() do exact ["WorkspaceId";"Root"] item;text "WorkspaceId" item|>ignore;text "Root" item|>ignore
                for item in c.GetProperty("BrowserPrincipals").EnumerateArray() do
                    exact ["PrincipalId";"KeyHashFile";"WorkspaceIds";"Revoked"] item
                    text "PrincipalId" item|>ignore;text "KeyHashFile" item|>ignore;boolean "Revoked" item|>ignore
                    for value in item.GetProperty("WorkspaceIds").EnumerateArray() do require(value.ValueKind=JsonValueKind.String)
                let session=c.GetProperty "BrowserSession"
                let names=["IdleSeconds";"AbsoluteSeconds";"MaximumSessions";"LoginAttemptsPerMinute";"LoginAdmission";"QueryAdmission";"QueryTimeoutSeconds"]
                exact names session
                for name in names do number name session|>ignore
            let sidecarDocument,sideHash=json (configPath+".native-collector.json") 16384L
            use sidecar=sidecarDocument
            let s=sidecar.RootElement
            exact ["Schema";"CredentialReference";"ExecutablePath";"CodexHome";"EvidenceRoot";"Provider";"Model";"Effort";"ExecutableSha256";"NativeVerifier"] s
            require(text "Schema" s="fsgg.telemetry.native-collector-installation/3")
            let reference=text "CredentialReference" s
            let credentials=c.GetProperty("Credentials").EnumerateArray()|>Seq.toArray
            require(credentials.Length>0&&credentials.Length<=4096)
            for item in credentials do
                exact ["Reference";"SecretFile";"WorkspaceId";"ProducerId";"StreamId";"Role";"GrantId";"GrantGeneration";"Revoked"] item
                for name in ["Reference";"SecretFile";"WorkspaceId";"ProducerId";"StreamId";"Role";"GrantId"] do text name item|>ignore
                number "GrantGeneration" item|>ignore;boolean "Revoked" item|>ignore
            let matches=credentials|>Array.filter(fun item->text "Reference" item=reference)
            require(matches.Length=1)
            let credential=matches[0]
            require(id reference&&text "Role" credential="native-collector"&&not(boolean "Revoked" credential)&&number "GrantGeneration" credential>0L)
            for name in ["WorkspaceId";"ProducerId";"StreamId";"GrantId"] do require(id(text name credential))
            // SecretFile is inert data: no descriptor, stat, credential bytes or grant operation.
            require(canonical(text "SecretFile" credential))
            let executable=text "ExecutablePath" s
            let _,exeHash,_=inspect executable false true (512L*1024L*1024L) false
            require(hash(text "ExecutableSha256" s)&&exeHash=text "ExecutableSha256" s)
            for name in ["Provider";"Model";"Effort"] do text name s|>ignore
            let home=text "CodexHome" s
            let evidence=text "EvidenceRoot" s
            for path in [home;evidence] do
                require(path.StartsWith(anchor+"/",StringComparison.Ordinal))
                directory path true
                let mutable parent=Path.GetDirectoryName path
                while parent<>anchor do directory parent true;parent<-Path.GetDirectoryName parent
            let receiptDocument,receiptHash=json (configPath+".native-collector.receipt.json") 65536L
            use receipt=receiptDocument
            let r=receipt.RootElement
            exact ["schema";"status";"ownerUid";"hostConfigSha256";"sidecarSha256";"executableSha256";"credentialReference";"workspaceId";"producerId";"streamId";"grantId";"grantGeneration";"sourceVerification";"snapshotOrigin";"sharedCostCompleteness";"activationAuthorized";"sourceReferenceSha256";"verifierRuntimeManifestSha256"] r
            require(text "schema" r="fsgg.telemetry.native-collector-installation-receipt/3"&&text "status" r="installed"&&number "ownerUid" r=int64 uid)
            for name,expected in ["hostConfigSha256",configHash;"sidecarSha256",sideHash;"executableSha256",exeHash;"credentialReference",reference;"workspaceId",text "WorkspaceId" credential;"producerId",text "ProducerId" credential;"streamId",text "StreamId" credential;"grantId",text "GrantId" credential] do require(text name r=expected)
            require(number "grantGeneration" r=number "GrantGeneration" credential&&not(boolean "activationAuthorized" r))
            for name in ["sourceVerification";"snapshotOrigin";"sharedCostCompleteness"] do require(text name r="unknown")
            let v=s.GetProperty "NativeVerifier"
            exact ["RuntimeExecutablePath";"RuntimeExecutableSha256";"ModulePath";"ModuleSha256";"RuntimeManifestPath";"RuntimeManifestSha256"] v
            let runtime=text "RuntimeExecutablePath" v
            let modulePath=text "ModulePath" v
            let manifestPath=text "RuntimeManifestPath" v
            require(text "ModuleSha256" v="8d6a33beae9a4de84fa7a703809e9b1a1656359a085f92091cf56de3b77fd3ba")
            require(runtime<>modulePath&&runtime<>manifestPath&&modulePath<>manifestPath)
            let manifestDocument,manifestHash=json manifestPath (1024L*1024L)
            use manifest=manifestDocument
            require(hash(text "RuntimeManifestSha256" v)&&manifestHash=text "RuntimeManifestSha256" v&&manifestHash=text "verifierRuntimeManifestSha256" r)
            let m=manifest.RootElement
            exact ["schema";"sourceRevision";"runtimeImageDigest";"runtimeExecutablePath";"modulePath";"files"] m
            require(text "schema" m="fsgg.telemetry.native-verifier-runtime/1"&&Regex.IsMatch(text "sourceRevision" m,@"\A[0-9a-f]{40}\z"))
            require(Regex.IsMatch(text "runtimeImageDigest" m,@"\Asha256:[0-9a-f]{64}\z")&&text "runtimeExecutablePath" m=runtime&&text "modulePath" m=modulePath)
            let rows=m.GetProperty("files").EnumerateArray()|>Seq.toArray
            require(rows.Length>0&&rows.Length<=4096)
            let mutable previous=""
            let mutable total=0L
            let mutable runtimeSeen=false
            let mutable moduleSeen=false
            for item in rows do
                exact ["path";"bytes";"sha256"] item
                let path=text "path" item
                let size=number "bytes" item
                let digest=text "sha256" item
                require(canonical path&&hash digest&&StringComparer.Ordinal.Compare(previous,path)<0&&size>=0L&&size<=512L*1024L*1024L-total)
                previous<-path;total<-total+size
                let metadata,actual,_=inspect path false (path=runtime) size false
                require(metadata.Length=size&&actual=digest)
                if size=0L then require(path<>runtime&&path<>modulePath&&path<>manifestPath&&digest="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"&&(metadata.Mode&&&0xfffu)=0x124u)
                if path=runtime then runtimeSeen<-true;require(size>0L&&digest=text "RuntimeExecutableSha256" v)
                if path=modulePath then moduleSeen<-true;require(size>0L&&digest=text "ModuleSha256" v)
            require(total>0L&&runtimeSeen&&moduleSeen)
            let sourceDocument,sourceHash=json (Path.Combine(anchor,"source-reference.json")) 65536L
            use source=sourceDocument
            let sr=source.RootElement
            exact ["schema";"profileSha256";"nativeSourceVolume";"developmentTarget";"collectorReadOnlyTarget";"readerProfileSha256";"captureQualified";"verifierRuntimeManifestSha256"] sr
            require(text "schema" sr="fsgg.telemetry.persistent-source-references/3"&&hash(text "profileSha256" sr)&&id(text "nativeSourceVolume" sr)&&canonical(text "developmentTarget" sr)&&text "collectorReadOnlyTarget" sr=home&&not(boolean "captureQualified" sr)&&text "verifierRuntimeManifestSha256" sr=manifestHash&&sourceHash=text "sourceReferenceSha256" r)
            let profilePath=Path.Combine(evidence,"fixed-native-capability-profile.json")
            let _,profileHash,profileBytes=inspect profilePath true false (1024L*1024L) true
            require(profileBytes.Length>0&&hash(text "readerProfileSha256" sr)&&profileHash=text "readerProfileSha256" sr)
            use profile=JsonDocument.Parse(profileBytes,JsonDocumentOptions(MaxDepth=32))
            duplicates profile.RootElement;require(profile.RootElement.ValueKind=JsonValueKind.Object)
            // Final rehash/custody observations reject drift after any earlier join.
            let originals=bound.ToArray()
            for path,metadata,digest in originals do
                let privateRequired=(metadata.Mode&&&0xfffu)=0x180u&&metadata.Uid=uid
                let observed,actual,_=inspect path privateRequired false metadata.Length false
                require(observed=metadata&&actual=digest)
            for path,metadata in directories do use held=descriptor path true in require(stamp held=metadata)
            let result=JsonSerializer.SerializeToUtf8Bytes {|schema="fsgg.telemetry.inactive-preparation-read/1";status="prepared-inactive";hostConfigSha256=configHash;managerReceiptSha256=receiptHash;sidecarSha256=sideHash;executableSha256=exeHash;sourceReferenceSha256=sourceHash;readerProfileSha256=profileHash;verifierRuntimeManifestSha256=manifestHash;sourceVerification="unknown";snapshotOrigin="unknown";sharedCostCompleteness="unknown";captureQualified=false;nativeAcceptanceClaimed=false;activationAuthorized=false|}
            0,Array.append result [|byte '\n'|]
        with _->2,failure
