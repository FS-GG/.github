namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions

type FileRow = { SourcePath:string; TargetPath:string; Bytes:int64; Sha256:string; Mode:string; SourceClass:string }
type ManifestFile = { SourcePath:string; TargetPath:string; Bytes:int64; Sha256:string; Mode:string }
type RoleManifest = { Schema:string; Role:string; AuthoritySha256:string; SearchRoots:string array; Argv:string array; LoaderPaths:string array; Files:ManifestFile array }
type RoleManifestReference = { Role:string; Path:string; Sha256:string }
type NativeAcquisitionInventory = { Schema:string; AcquisitionProvenanceSha256:string; ProfileSha256:string; ReaderProfileSha256:string; NativeExecutable:string; NativeExecutableSha256:string; PythonExecutable:string; PythonVersion:string; VerifierArgv:string array; SearchRoots:string array; LoaderPath:string; ImportPaths:string array; LibraryPaths:string array; OsDataPaths:string array; Files:ManifestFile array }
type TrustedNativeSelection = { Schema:string; AcquisitionProvenanceSha256:string; NativeInventorySha256:string; ProfileSha256:string; ReaderProfileSha256:string; NativeExecutableSha256:string; PythonExecutable:string; PythonVersion:string; VerifierArgv:string array; SearchRoots:string array; LoaderPath:string; ImportPaths:string array; LibraryPaths:string array; OsDataPaths:string array }
type Selection = { IdentityClass:string; AcquisitionRoot:string; ManifestRoot:string; RoleManifests:RoleManifestReference array; NativeInventoryPath:string; NativeInventorySha256:string; OwnerUid:int; Platform:string; HostSourceRevision:string; HostReleaseId:int64; HostRunId:int64; HostArchiveSha256:string; HostPackageSha256:string; HostPayloadSha256:string; HostManifestSha256:string; HostJournalSha256:string; ManagerSourceRevision:string; ManagerSourceTree:string; ManagerArtifactSha256:string; ManagerManifestSha256:string; ManagerPreparedSha256:string; ManagerArchiveSha256:string; ManagerRunId:int64; ManagerArtifactId:int64; RuntimeImageDigest:string; RuntimeTreeSha256:string; NativeElfSha256:string; NativeProfileSha256:string; ReaderProfileSha256:string; CanonicalVerifierSha256:string; InventorySha256:string; Files:FileRow array }
type ClosureResult = Unavailable of string | Refused of string | Prepared of byte array

module ImageClosure =
    [<Literal>]
    let HostPackageSha256="7ad2c30894cb3eafbf498e5d247034bc3167ee30dd07c8b09c6b4915657c598a"
    [<Literal>]
    let HostSourceRevision="f43e0a1f94448aa8f7668b1ed72f3169a7cf925e"
    [<Literal>]
    let HostArchiveSha256="c7cbaa474fdcd0ba577f8577ec92bb381f032db7842f86ddb1b3fb050d8a97ad"
    [<Literal>]
    let HostPayloadSha256="5572aa61f284abc5a37a12aa88f99b5169c4379be2aa9959e46c9934532f2836"
    [<Literal>]
    let HostManifestSha256="7d61b4888d08299b5578df2e3e283dee88b5acabafa53490b4ba8d00e6676704"
    [<Literal>]
    let HostJournalSha256="ec63822c627cfad490f9eea731257ac531c9ac3bd681f4790b9489dcfc8470ce"
    [<Literal>]
    let ManagerArtifactSha256="518041591b9b7f1827911f0e796a1815799841831b962d3112169d9241969cdd"
    [<Literal>]
    let ManagerManifestSha256="966e13e827b3b3a3f37c51bcc35fd57f4684741d19a3308905e5f66ac51ebf0e"
    [<Literal>]
    let ManagerSourceRevision="49fe964f0239ad3734f5fa5119b3227f2a04758d"
    [<Literal>]
    let ManagerSourceTree="36b0be5cc74bfd10d045ab11ce978547464d5266"
    [<Literal>]
    let ManagerPreparedSha256="287ea427ec040f4fc4b3791fbea7cfb6dd765a9ee45584dacffd960cc10c5d1a"
    [<Literal>]
    let ManagerArchiveSha256="03d46e6553e99be27c0bbc06d8767e2aa2a5e9b588d608d4b4c229e7a87ad74f"
    [<Literal>]
    let RuntimeImageDigest="sha256:ed6a2d26633ddcd3d42a1d9f9866214ecbbc11ba6ac5e0e843da02c13da24072"
    [<Literal>]
    let RuntimeTreeSha256="ead4ece42719198be9607d18415e428e3a6fcaadf50b88dc6e93894c47bec4c2"
    [<Literal>]
    let NativeElfSha256="167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9"
    [<Literal>]
    let NativeProfileSha256="1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34"
    [<Literal>]
    let CanonicalVerifierSha256="8d6a33beae9a4de84fa7a703809e9b1a1656359a085f92091cf56de3b77fd3ba"
    let private sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let private sha64 (value:string)=not(String.IsNullOrWhiteSpace value)&&Regex.IsMatch(value,"^[0-9a-f]{64}$")&&value<>String('0',64)
    let private emptySha256="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    let private validBytes role bytes digest mode =
        bytes>0L||(role="native"&&bytes=0L&&digest=emptySha256&&mode="0444")
    let private canonicalSegments (value:string)=
        let segments=value.Split('/')
        segments.Length<=64&&(segments|>Array.forall(fun segment->segment<>"."&&segment<>".."&&System.Text.Encoding.UTF8.GetByteCount(segment)<=255))
    let private canonicalPath absolute (value:string)=
        not(String.IsNullOrWhiteSpace value)&&System.Text.Encoding.UTF8.GetByteCount(value)<=4096&&
        Regex.IsMatch(value,(if absolute then @"\A/[\p{L}\p{M}0-9._+=-]+(?:/[\p{L}\p{M}0-9._+=-]+)*\z" else @"\A[\p{L}\p{M}0-9._+=-]+(?:/[\p{L}\p{M}0-9._+=-]+)*\z"))&&
        canonicalSegments (if absolute then value.Substring(1) else value)
    let private relativePath (value:string)=canonicalPath false value
    let private canonicalAbsolute (value:string)=canonicalPath true value
    let private targetPath role (value:string)=
        canonicalAbsolute value&&
        (value.StartsWith($"/opt/fsgg/image/{role}/",StringComparison.Ordinal)||
         match role with
         | "host"->value.StartsWith("/opt/fsgg/telemetry-host/",StringComparison.Ordinal)
         | "manager"->value.StartsWith("/opt/fsgg/telemetry-host-manager/",StringComparison.Ordinal)
         | "runtime"->value.StartsWith("/usr/share/dotnet/",StringComparison.Ordinal)
         | "native"->[|"/opt/fsgg/";"/usr/local/";"/usr/lib/";"/lib/";"/lib64/";"/etc/";"/usr/share/"|]|>Array.exists(fun prefix->value.StartsWith(prefix,StringComparison.Ordinal))
         | _->false)
    let private fixedProduction selection = selection.Platform="linux/amd64"&&selection.OwnerUid=0&&selection.HostSourceRevision=HostSourceRevision&&selection.HostReleaseId=401538149L&&selection.HostRunId=36964135216L&&selection.HostArchiveSha256=HostArchiveSha256&&selection.HostPackageSha256=HostPackageSha256&&selection.HostPayloadSha256=HostPayloadSha256&&selection.HostManifestSha256=HostManifestSha256&&selection.HostJournalSha256=HostJournalSha256&&selection.ManagerSourceRevision=ManagerSourceRevision&&selection.ManagerSourceTree=ManagerSourceTree&&selection.ManagerArtifactSha256=ManagerArtifactSha256&&selection.ManagerManifestSha256=ManagerManifestSha256&&selection.ManagerPreparedSha256=ManagerPreparedSha256&&selection.ManagerArchiveSha256=ManagerArchiveSha256&&selection.ManagerRunId=36983338783L&&selection.ManagerArtifactId=11216418410L&&selection.RuntimeImageDigest=RuntimeImageDigest&&selection.RuntimeTreeSha256=RuntimeTreeSha256&&selection.NativeElfSha256=NativeElfSha256&&selection.NativeProfileSha256=NativeProfileSha256&&selection.CanonicalVerifierSha256=CanonicalVerifierSha256
    let private modeText (path:string) = Convert.ToString(int(File.GetUnixFileMode path),8).PadLeft(4,'0')
    let private noLink (info:FileSystemInfo)=String.IsNullOrEmpty info.LinkTarget
    let ownerUid (path:string) =
        let start=ProcessStartInfo("/usr/bin/stat")
        start.ArgumentList.Add("--format=%u")
        start.ArgumentList.Add(path)
        start.RedirectStandardOutput<-true
        start.UseShellExecute<-false
        use child=Process.Start start
        let output=child.StandardOutput.ReadToEnd().Trim()
        child.WaitForExit()
        match child.ExitCode,Int32.TryParse output with 0,(true,value)->value|_-> -1
    let private regularFile (path:string) =
        let start=ProcessStartInfo("/usr/bin/stat")
        start.ArgumentList.Add("--format=%F")
        start.ArgumentList.Add(path)
        start.RedirectStandardOutput<-true
        start.UseShellExecute<-false
        use child=Process.Start start
        let output=child.StandardOutput.ReadToEnd().Trim()
        child.WaitForExit()
        child.ExitCode=0&&(output="regular file"||output="regular empty file")
    let private custody root source expectedUid =
        let rec directories current =
            let info=DirectoryInfo current
            if not info.Exists||not(noLink info)||ownerUid current<>expectedUid||((File.GetUnixFileMode current)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then false
            elif current=root then true
            elif isNull info.Parent then false
            else directories info.Parent.FullName
        let file=FileInfo source
        noLink file&&ownerUid source=expectedUid&&directories file.DirectoryName
    let private boundedCensus root expectedUid =
        let files=ResizeArray<string>()
        let pending=Collections.Generic.Stack<string>()
        pending.Push root
        let mutable error=None
        let mutable visited=1
        while pending.Count>0&&error.IsNone do
            let directory=pending.Pop()
            let info=DirectoryInfo directory
            if not info.Exists||not(noLink info)||ownerUid directory<>expectedUid||((File.GetUnixFileMode directory)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then error<-Some "acquisition-directory-custody"
            else
                use entries=info.EnumerateFileSystemInfos().GetEnumerator()
                let mutable more=true
                while error.IsNone&&more do
                    more<-entries.MoveNext()
                    if more then
                      let entry=entries.Current
                      visited<-visited+1
                      if visited>8192 then error<-Some "acquisition-census-bound"
                      elif not(noLink entry)||entry.Attributes.HasFlag FileAttributes.ReparsePoint then error<-Some "acquisition-census-link"
                      elif entry.Attributes.HasFlag FileAttributes.Directory then pending.Push entry.FullName
                      elif regularFile entry.FullName then files.Add(Path.GetRelativePath(root,entry.FullName))
                      else error<-Some "acquisition-census-special-file"
        match error with Some value->Error value|None->Ok(files|>Set.ofSeq)
    let private physicalRows selection =
        try
            let root=Path.GetFullPath selection.AcquisitionRoot
            let rootInfo=DirectoryInfo root
            if not rootInfo.Exists||not(noLink rootInfo)||ownerUid root<>selection.OwnerUid||((File.GetUnixFileMode root)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then Error "acquisition-root-custody"
            else match boundedCensus root selection.OwnerUid with
                 | Error reason->Error reason
                 | Ok census when census<>(selection.Files|>Array.map _.SourcePath|>Set.ofArray)->Error "acquisition-root-inventory-incomplete"
                 | Ok _ ->
                    selection.Files|>Array.map(fun row->
                        let source=Path.GetFullPath(Path.Combine(root,row.SourcePath))
                        let prefix=root.TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
                        if not(source.StartsWith(prefix,StringComparison.Ordinal)) then Error "inventory-path"
                        else
                            let info=FileInfo source
                            if not info.Exists||not(noLink info)||info.Attributes.HasFlag FileAttributes.ReparsePoint||not(regularFile source)||not(custody root source selection.OwnerUid) then Error "inventory-regular-file-or-custody"
                            elif info.Length<>row.Bytes||modeText source<>row.Mode||sha(File.ReadAllBytes source)<>row.Sha256 then Error "inventory-physical-mismatch"
                            else Ok(source,row))
                    |>Array.fold(fun state next->match state,next with|Error e,_->Error e|_,Error e->Error e|Ok rows,Ok row->Ok(row::rows))(Ok [])
                    |>Result.map(List.rev>>List.toArray)
        with _ -> Error "inventory-physical-read"
    let private validateManifests selection (rows:FileRow array) =
        try
            let root=Path.GetFullPath selection.ManifestRoot
            if selection.RoleManifests.Length<>4||(selection.RoleManifests|>Array.map _.Role|>Set.ofArray)<>set["host";"manager";"native";"runtime"] then Error "role-manifest-set"
            else
              selection.RoleManifests|>Array.map(fun reference->
                if not(relativePath reference.Path)||not(sha64 reference.Sha256) then Error "role-manifest-reference" else
                let path=Path.GetFullPath(Path.Combine(root,reference.Path))
                let prefix=root.TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
                let info=FileInfo path
                if not(path.StartsWith(prefix,StringComparison.Ordinal))||not info.Exists||info.Length<=0L||info.Length>2L*1024L*1024L||not(custody root path selection.OwnerUid) then Error "role-manifest-physical" else
                let bytes=File.ReadAllBytes path
                if sha bytes<>reference.Sha256 then Error "role-manifest-digest" else
                use document=JsonDocument.Parse bytes
                let rootElement=document.RootElement
                let names=rootElement.EnumerateObject()|>Seq.map _.Name|>Seq.toArray
                let mutable manifestFiles=Unchecked.defaultof<JsonElement>
                if rootElement.ValueKind<>JsonValueKind.Object||names.Length<>7||Set.ofArray names<>set["Schema";"Role";"AuthoritySha256";"SearchRoots";"Argv";"LoaderPaths";"Files"]||not(rootElement.TryGetProperty("Files",&manifestFiles))||manifestFiles.ValueKind<>JsonValueKind.Array||manifestFiles.GetArrayLength()=0||manifestFiles.GetArrayLength()>8192||(manifestFiles.EnumerateArray()|>Seq.exists(fun item->item.ValueKind<>JsonValueKind.Object||(item.EnumerateObject()|>Seq.length)<>5||(item.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq)<>set["SourcePath";"TargetPath";"Bytes";"Sha256";"Mode"])) then Error "role-manifest-not-closed" else
                let manifest=JsonSerializer.Deserialize<RoleManifest> bytes
                let authority=match reference.Role with "host"->selection.HostManifestSha256|"manager"->selection.ManagerManifestSha256|"runtime"->selection.RuntimeTreeSha256|_->selection.NativeProfileSha256
                let expected=rows|>Array.filter(fun row->row.SourceClass=reference.Role)|>Array.map(fun row->{SourcePath=row.SourcePath;TargetPath=row.TargetPath;Bytes=row.Bytes;Sha256=row.Sha256;Mode=row.Mode})
                let invalidNativeLayout =
                    manifest.Argv.Length<>1 ||
                    not(expected|>Array.exists(fun file->file.TargetPath=manifest.Argv[0])) ||
                    manifest.LoaderPaths.Length=0 ||
                    (manifest.LoaderPaths|>Array.distinct).Length<>manifest.LoaderPaths.Length ||
                    (manifest.LoaderPaths|>Array.exists(fun value->not(rows|>Array.exists(fun row->row.SourceClass="runtime"&&row.TargetPath=value))))
                if manifest.Schema<>"fsgg.telemetry.persistent-v3-role-closure/1"||manifest.Role<>reference.Role||manifest.AuthoritySha256<>authority||manifest.Files<>expected then Error "role-manifest-content"
                elif manifest.SearchRoots<>[|$"/opt/fsgg/image/{reference.Role}"|] then Error "role-manifest-search-roots"
                elif reference.Role="native"&&invalidNativeLayout then Error "native-layout"
                elif reference.Role<>"native"&&(manifest.Argv.Length<>0||manifest.LoaderPaths.Length<>0) then Error "role-layout"
                else Ok manifest)
              |>Array.fold(fun state next->match state,next with Error e,_->Error e|_,Error e->Error e|Ok values,Ok value->Ok(value::values))(Ok[])
              |>Result.map(List.rev>>List.toArray)
        with _->Error "role-manifest-read"
    let private productionManifest selection role expectedSha =
        try
            let reference=selection.RoleManifests|>Array.find(fun item->item.Role=role)
            if reference.Sha256<>expectedSha||not(relativePath reference.Path) then Error "production-authority-reference" else
            let root=Path.GetFullPath selection.ManifestRoot
            let path=Path.GetFullPath(Path.Combine(root,reference.Path))
            let prefix=root.TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
            let info=FileInfo path
            if not(path.StartsWith(prefix,StringComparison.Ordinal))||not info.Exists||info.Length<=0L||info.Length>2L*1024L*1024L||not(custody root path selection.OwnerUid) then Error "production-authority-physical" else
            let bytes=File.ReadAllBytes path
            if sha bytes<>expectedSha then Error "production-authority-digest" else
            use document=JsonDocument.Parse bytes
            Ok(document.RootElement.Clone())
        with _->Error "production-authority-read"
    let private nativeAcquisitionInventory (trusted:TrustedNativeSelection) selection =
        try
            if not(relativePath selection.NativeInventoryPath)||not(sha64 selection.NativeInventorySha256) then Error "native-authority-inventory-reference" else
            if selection.NativeInventorySha256<>trusted.NativeInventorySha256 then Error "native-authority-inventory-selection-mismatch" else
            let root=Path.GetFullPath selection.ManifestRoot
            let path=Path.GetFullPath(Path.Combine(root,selection.NativeInventoryPath))
            let prefix=root.TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
            let info=FileInfo path
            if not(path.StartsWith(prefix,StringComparison.Ordinal))||not info.Exists||info.Length<=0L||info.Length>2L*1024L*1024L||not(custody root path selection.OwnerUid) then Error "native-authority-inventory-physical" else
            let bytes=File.ReadAllBytes path
            if sha bytes<>trusted.NativeInventorySha256 then Error "native-authority-inventory-digest" else
            use document=JsonDocument.Parse bytes
            let value=document.RootElement
            let expected=set["Schema";"AcquisitionProvenanceSha256";"ProfileSha256";"ReaderProfileSha256";"NativeExecutable";"NativeExecutableSha256";"PythonExecutable";"PythonVersion";"VerifierArgv";"SearchRoots";"LoaderPath";"ImportPaths";"LibraryPaths";"OsDataPaths";"Files"]
            let names=if value.ValueKind=JsonValueKind.Object then value.EnumerateObject()|>Seq.map _.Name|>Seq.toArray else [||]
            let mutable files=Unchecked.defaultof<JsonElement>
            let arrays=[|"VerifierArgv";"SearchRoots";"ImportPaths";"LibraryPaths";"OsDataPaths"|]
            if names.Length<>expected.Count||Set.ofArray names<>expected||not(value.TryGetProperty("Files",&files))||files.ValueKind<>JsonValueKind.Array||files.GetArrayLength()=0||files.GetArrayLength()>8192 then Error "native-authority-inventory-not-closed"
            elif arrays|>Array.exists(fun name->let item=value.GetProperty name in item.ValueKind<>JsonValueKind.Array||(item.EnumerateArray()|>Seq.exists(fun entry->entry.ValueKind<>JsonValueKind.String))) then Error "native-authority-inventory-not-closed"
            elif files.EnumerateArray()|>Seq.exists(fun item->let itemNames=if item.ValueKind=JsonValueKind.Object then item.EnumerateObject()|>Seq.map _.Name|>Seq.toArray else [||] in itemNames.Length<>5||Set.ofArray itemNames<>set["SourcePath";"TargetPath";"Bytes";"Sha256";"Mode"]) then Error "native-authority-inventory-not-closed"
            else Ok(JsonSerializer.Deserialize<NativeAcquisitionInventory> bytes)
        with _->Error "native-authority-inventory-read"
    let private manifestFiles (sourcePrefix:string) (targetRoot:string) (value:JsonElement) =
        if value.ValueKind<>JsonValueKind.Array||value.GetArrayLength()=0||value.GetArrayLength()>8192 then Error "production-authority-files" else
        value.EnumerateArray()
        |>Seq.map(fun item->
            let names=item.EnumerateObject()|>Seq.map _.Name|>Seq.toArray
            if item.ValueKind<>JsonValueKind.Object||names.Length<>4||Set.ofArray names<>set["bytes";"mode";"path";"sha256"] then Error "production-authority-file-shape" else
            let path=item.GetProperty("path").GetString()
            let relative=if String.IsNullOrEmpty sourcePrefix then path elif path.StartsWith(sourcePrefix+"/",StringComparison.Ordinal) then path.Substring(sourcePrefix.Length+1) else path
            let mode=item.GetProperty("mode").GetString()
            let digest=item.GetProperty("sha256").GetString()
            let bytes=item.GetProperty("bytes").GetInt64()
            if not(relativePath relative)||bytes<=0L||(mode<>"0444"&&mode<>"0555")||not(sha64 digest) then Error "production-authority-file-value"
            else Ok {SourcePath=(if String.IsNullOrEmpty sourcePrefix then relative else sourcePrefix+"/"+relative);TargetPath=targetRoot.TrimEnd('/')+"/"+relative;Bytes=bytes;Sha256=digest;Mode=mode})
        |>Seq.fold(fun state next->match state,next with Error e,_->Error e|_,Error e->Error e|Ok values,Ok value->Ok(value::values))(Ok[])
        |>Result.map(List.rev>>List.toArray)
    let private productionManifests (trusted:TrustedNativeSelection) selection (rows:FileRow array) =
        let pinned=selection.IdentityClass="production"
        let rowSet role=rows|>Array.filter(fun row->row.SourceClass=role)|>Array.map(fun row->{SourcePath=row.SourcePath;TargetPath=row.TargetPath;Bytes=row.Bytes;Sha256=row.Sha256;Mode=row.Mode})
        let closed expected (value:JsonElement)=value.ValueKind=JsonValueKind.Object&&(value.EnumerateObject()|>Seq.length)=Set.count expected&&(value.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq)=expected
        match productionManifest selection "host" selection.HostManifestSha256,productionManifest selection "manager" selection.ManagerManifestSha256,productionManifest selection "runtime" selection.RuntimeTreeSha256,productionManifest selection "native" selection.NativeProfileSha256,nativeAcquisitionInventory trusted selection with
        | Ok host,Ok manager,Ok runtime,Ok native,Ok nativeInventory->
            try
                let hostNames=host.EnumerateObject()|>Seq.map _.Name|>Set.ofSeq
                let hostExpected=set["archiveSha256";"createdAt";"dependencyLockSha256";"framework";"packageId";"producerPayloadSha256";"runtimePrerequisites";"schema";"sourceSha";"supportedStoreSchemaMax";"supportedStoreSchemaMin";"tag";"target";"uiAssetTreeSha256";"version"]
                let hostRows=rowSet "host"
                let hostOk=closed hostExpected host&&hostNames=hostExpected&&host.GetProperty("schema").GetString()="fsgg.telemetry.host-release/1"&&host.GetProperty("sourceSha").GetString()=selection.HostSourceRevision&&host.GetProperty("archiveSha256").GetString()=selection.HostArchiveSha256&&host.GetProperty("producerPayloadSha256").GetString()="sha256:"+selection.HostPayloadSha256&&host.GetProperty("version").GetString()="0.3.0"&&host.GetProperty("target").GetString()="linux-x64"&&hostRows.Length=1&&hostRows[0].SourcePath="host/FS.GG.Telemetry.Host.0.3.0.nupkg"&&hostRows[0].TargetPath="/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.0.3.0.nupkg"&&hostRows[0].Sha256=selection.HostArchiveSha256&&(not pinned||hostRows[0].Bytes=6074745L)&&hostRows[0].Mode="0444"
                if not hostOk then Error "production-host-authority" else
                let managerExpected=set["archiveRoot";"buildSdk";"entrypoint";"fixedArgv";"installationRoot";"payloads";"runtime";"schema";"source"]
                if not(closed managerExpected manager)||manager.GetProperty("schema").GetString()<>"fsgg.coordination.telemetry-host-manager-bundle/2" then Error "production-manager-schema" else
                let source=manager.GetProperty("source")
                let runtimeNode=manager.GetProperty("runtime")
                let sourceExpected=set["lockFile";"project";"repository";"revision";"tree"]
                let runtimeExpected=set["aspnetFramework";"aspnetFrameworkVersion";"dotnetRoot";"files";"framework";"frameworkVersion";"rollForward";"source";"treeSha256"]
                let archiveRoot=manager.GetProperty("archiveRoot").GetString()
                let installationRoot=manager.GetProperty("installationRoot").GetString()
                let fixedArgv=manager.GetProperty("fixedArgv").EnumerateArray()|>Seq.map _.GetString()|>Seq.toArray
                let expectedArgv=[|"/usr/share/dotnet/dotnet";"exec";"--fx-version";"10.0.12";"/opt/fsgg/telemetry-host-manager/TelemetryHostManager.dll"|]
                if not(closed sourceExpected source)||not(closed runtimeExpected runtimeNode)||source.GetProperty("revision").GetString()<>selection.ManagerSourceRevision||source.GetProperty("tree").GetString()<>selection.ManagerSourceTree||archiveRoot<>"telemetry-host-manager-net10.0"||installationRoot<>"/opt/fsgg/telemetry-host-manager"||fixedArgv<>expectedArgv then Error "production-manager-identity-layout" else
                match manifestFiles archiveRoot installationRoot (manager.GetProperty("payloads")),manifestFiles "runtime" "/usr/share/dotnet" runtime with
                | Ok managerFiles,Ok runtimeFiles->
                    let embeddedRuntime=runtimeNode.GetProperty("files")
                    match manifestFiles "runtime" "/usr/share/dotnet" embeddedRuntime with
                    | Error reason->Error reason
                    | Ok embeddedFiles when embeddedFiles<>runtimeFiles||runtimeNode.GetProperty("treeSha256").GetString()<>selection.RuntimeTreeSha256||runtimeNode.GetProperty("dotnetRoot").GetString()<>"/usr/share/dotnet"||runtimeNode.GetProperty("source").GetProperty("manifestDigest").GetString()<>selection.RuntimeImageDigest->Error "production-runtime-authority"
                    | Ok _ when managerFiles<>rowSet "manager"||runtimeFiles<>rowSet "runtime"->Error "production-authority-inventory-mismatch"
                    | Ok _->
                        let nativeRows=rowSet "native"
                        let nativeNode=native.GetProperty("native")
                        let nativeExpected=set["config";"configSha256";"bytes";"executable";"protocolSchemaSha256";"sessionFlagsSha256";"sha256";"version"]
                        let nativeTopExpected=set["effort";"model";"native";"network";"operationId";"producer";"prompt";"provider";"runtime";"schema";"supportedOperations"]
                        let nativeExecutable=nativeNode.GetProperty("executable").GetString()
                        let nativeDigest=nativeNode.GetProperty("sha256").GetString()
                        let nativeBytes=nativeNode.GetProperty("bytes").GetInt64()
                        let config=nativeNode.GetProperty("config").GetString()
                        let configDigest=nativeNode.GetProperty("configSha256").GetString()
                        let has digest target= nativeRows|>Array.exists(fun row->row.Sha256=digest&&row.TargetPath=target)
                        let nativeIdentity=closed nativeTopExpected native&&closed nativeExpected nativeNode&&native.GetProperty("schema").GetString()="fsgg.telemetry.native-operation-profile/1"&&nativeExecutable="/opt/fsgg/codex/codex"&&nativeDigest=selection.NativeElfSha256&&(not pinned||nativeBytes=286594376L)&&(nativeRows|>Array.exists(fun row->row.Sha256=nativeDigest&&row.TargetPath=nativeExecutable&&row.Bytes=nativeBytes))&&has selection.NativeProfileSha256 "/opt/fsgg/profile/native-operation-v1.json"&&has selection.ReaderProfileSha256 "/opt/fsgg/profile/reader-profile.json"&&has selection.CanonicalVerifierSha256 "/opt/fsgg/verifier/learn_01_native_source.py"&&has configDigest config&&has (nativeNode.GetProperty("protocolSchemaSha256").GetString()) "/opt/fsgg/native-protocol-schema.json"&&has (nativeNode.GetProperty("sessionFlagsSha256").GetString()) "/opt/fsgg/native-session-flags.json"
                        let authorityFiles=nativeInventory.Files
                        let authorityTargets=authorityFiles|>Array.map _.TargetPath|>Set.ofArray
                        let canonicalList (values:string array)=values.Length>0&&(values|>Array.distinct).Length=values.Length&&values=Array.sort values&&(values|>Array.forall canonicalAbsolute)
                        let underSearchRoot (path:string)=nativeInventory.SearchRoots|>Array.exists(fun root->path.StartsWith(root.TrimEnd('/')+"/",StringComparison.Ordinal))
                        let fixedVerifierArgv=[|nativeInventory.PythonExecutable;"-I";"-S";"-B";"/opt/fsgg/verifier/learn_01_native_source.py";"verify"|]
                        let layoutPaths=Array.concat[ [|nativeInventory.NativeExecutable;nativeInventory.PythonExecutable;nativeInventory.LoaderPath|];nativeInventory.ImportPaths;nativeInventory.LibraryPaths;nativeInventory.OsDataPaths ]
                        let executable path digest=authorityFiles|>Array.exists(fun row->row.TargetPath=path&&row.Sha256=digest&&row.Mode="0555")
                        let nativeAuthority=
                            nativeInventory.Schema="fsgg.telemetry.native-acquisition-inventory/1"&&
                            nativeInventory.AcquisitionProvenanceSha256=trusted.AcquisitionProvenanceSha256&&
                            nativeInventory.ProfileSha256=trusted.ProfileSha256&&nativeInventory.ProfileSha256=selection.NativeProfileSha256&&
                            nativeInventory.ReaderProfileSha256=trusted.ReaderProfileSha256&&nativeInventory.ReaderProfileSha256=selection.ReaderProfileSha256&&
                            nativeInventory.NativeExecutable=nativeExecutable&&nativeInventory.NativeExecutableSha256=trusted.NativeExecutableSha256&&nativeInventory.NativeExecutableSha256=selection.NativeElfSha256&&
                            nativeInventory.PythonExecutable=trusted.PythonExecutable&&nativeInventory.PythonVersion=trusted.PythonVersion&&
                            nativeInventory.VerifierArgv=trusted.VerifierArgv&&nativeInventory.VerifierArgv=fixedVerifierArgv&&nativeInventory.SearchRoots=trusted.SearchRoots&&
                            nativeInventory.LoaderPath=trusted.LoaderPath&&nativeInventory.ImportPaths=trusted.ImportPaths&&nativeInventory.LibraryPaths=trusted.LibraryPaths&&nativeInventory.OsDataPaths=trusted.OsDataPaths&&
                            canonicalAbsolute nativeInventory.LoaderPath&&canonicalList nativeInventory.ImportPaths&&canonicalList nativeInventory.LibraryPaths&&canonicalList nativeInventory.OsDataPaths&&
                            (layoutPaths|>Array.distinct).Length=layoutPaths.Length&&
                            (nativeInventory.ImportPaths|>Array.forall underSearchRoot)&&
                            executable nativeInventory.NativeExecutable nativeInventory.NativeExecutableSha256&&
                            (authorityFiles|>Array.exists(fun row->row.TargetPath=nativeInventory.PythonExecutable&&row.Mode="0555"))&&
                            (layoutPaths|>Array.forall authorityTargets.Contains)&&
                            authorityFiles=(authorityFiles|>Array.sortBy _.TargetPath)&&
                            (authorityFiles|>Array.distinctBy _.TargetPath).Length=authorityFiles.Length&&
                            (authorityFiles|>Array.distinctBy _.SourcePath).Length=authorityFiles.Length&&
                            (authorityFiles|>Array.forall(fun row->relativePath row.SourcePath&&targetPath "native" row.TargetPath&&validBytes "native" row.Bytes row.Sha256 row.Mode&&sha64 row.Sha256&&(row.Mode="0444"||row.Mode="0555")))&&
                            authorityFiles=nativeRows
                        if not nativeIdentity then Error "production-native-profile"
                        elif not nativeAuthority then Error "production-native-authority-inventory-mismatch"
                        else
                          let mk role authority search argv loaders files={Schema="fsgg.telemetry.persistent-v3-role-closure/1";Role=role;AuthoritySha256=authority;SearchRoots=search;Argv=argv;LoaderPaths=loaders;Files=files}
                          let loaders=Array.concat[[|nativeInventory.LoaderPath|];nativeInventory.LibraryPaths]
                          Ok [|mk "host" selection.HostManifestSha256 [|"/opt/fsgg/telemetry-host"|] [||] [||] hostRows;mk "manager" selection.ManagerManifestSha256 [|installationRoot|] fixedArgv [|"/usr/share/dotnet"|] managerFiles;mk "native" selection.NativeProfileSha256 nativeInventory.SearchRoots [|nativeExecutable|] loaders nativeRows;mk "runtime" selection.RuntimeTreeSha256 [|"/usr/share/dotnet"|] [||] [||] runtimeFiles|]
                | Error reason,_->Error reason|_,Error reason->Error reason
            with _->Error "production-authority-shape"
        | Error reason,_,_,_,_->Error reason|_,Error reason,_,_,_->Error reason|_,_,Error reason,_,_->Error reason|_,_,_,Error reason,_->Error reason|_,_,_,_,Error reason->Error reason
    let private trustedNativeShape (trusted:TrustedNativeSelection) =
        let canonicalList (values:string array)=values.Length>0&&(values|>Array.distinct).Length=values.Length&&values=Array.sort values&&(values|>Array.forall canonicalAbsolute)
        trusted.Schema="fsgg.telemetry.trusted-native-selection/1"&&sha64 trusted.AcquisitionProvenanceSha256&&sha64 trusted.NativeInventorySha256&&sha64 trusted.ProfileSha256&&sha64 trusted.ReaderProfileSha256&&sha64 trusted.NativeExecutableSha256&&
        trusted.PythonExecutable="/opt/fsgg/python/bin/python3.14"&&trusted.PythonVersion="3.14.0"&&trusted.VerifierArgv=[|trusted.PythonExecutable;"-I";"-S";"-B";"/opt/fsgg/verifier/learn_01_native_source.py";"verify"|]&&trusted.SearchRoots=[|"/opt/fsgg/python/lib/python3.14"|]&&canonicalAbsolute trusted.LoaderPath&&canonicalList trusted.ImportPaths&&canonicalList trusted.LibraryPaths&&canonicalList trusted.OsDataPaths
    let prepareWithTrustedNative (trusted:TrustedNativeSelection option) selection =
        if String.IsNullOrWhiteSpace selection.InventorySha256||selection.Files.Length=0 then Unavailable "native-image-closure-inventory-acquisition-required"
        elif selection.IdentityClass<>"production"&&selection.IdentityClass<>"production-contract-test"&&selection.IdentityClass<>"synthetic-test" then Refused "identity-class"
        elif selection.IdentityClass="production"&&not(fixedProduction selection) then Refused "fixed-production-identity-mismatch"
        elif selection.IdentityClass<>"synthetic-test"&&trusted.IsNone then Unavailable "trusted-native-selection-acquisition-required"
        elif selection.IdentityClass<>"synthetic-test"&&not(trustedNativeShape trusted.Value) then Refused "trusted-native-selection-shape"
        elif selection.IdentityClass<>"synthetic-test"&&(String.IsNullOrWhiteSpace selection.NativeInventoryPath||not(sha64 selection.NativeInventorySha256)) then Unavailable "native-authority-inventory-acquisition-required"
        elif selection.Platform<>"linux/amd64"||not(sha64 selection.ReaderProfileSha256&&sha64 selection.InventorySha256) then Refused "selection-shape"
        else
            let sorted=selection.Files|>Array.sortBy _.TargetPath
            if sorted<>selection.Files||(sorted|>Array.distinctBy _.TargetPath).Length<>sorted.Length||(sorted|>Array.distinctBy _.SourcePath).Length<>sorted.Length then Refused "inventory-order-or-duplicate"
            elif sorted.Length>8192||(sorted|>Array.exists(fun row->row.Bytes>1024L*1024L*1024L)) then Refused "inventory-bound"
            elif sorted|>Array.exists(fun row->not(relativePath row.SourcePath)||not(targetPath row.SourceClass row.TargetPath)||not(validBytes row.SourceClass row.Bytes row.Sha256 row.Mode)||not(sha64 row.Sha256)||(row.Mode<>"0444"&&row.Mode<>"0555")||not(Set.contains row.SourceClass (set["host";"manager";"native";"runtime"]))) then Refused "inventory-entry"
            elif (sorted|>Array.sumBy _.Bytes)<=0L||(sorted|>Array.sumBy _.Bytes)>1024L*1024L*1024L then Refused "inventory-bound"
            elif (sorted|>Array.map _.SourceClass|>Set.ofArray)<>set["host";"manager";"native";"runtime"] then Refused "inventory-omits-dependency-root"
            elif sha(JsonSerializer.SerializeToUtf8Bytes sorted)<>selection.InventorySha256 then Refused "inventory-digest"
            else match physicalRows selection with
                 | Error reason->Refused reason
                 | Ok acquired->
                    let requiredProductionDigests=[|selection.HostArchiveSha256;selection.NativeElfSha256;selection.NativeProfileSha256;selection.ReaderProfileSha256;selection.CanonicalVerifierSha256|]
                    if selection.IdentityClass<>"synthetic-test" && not(requiredProductionDigests|>Array.forall(fun digest->acquired|>Array.exists(fun(_,row)->row.Sha256=digest))) then Refused "inventory-omits-production-code-or-profile"
                    else match (if selection.IdentityClass<>"synthetic-test" then productionManifests trusted.Value selection sorted else validateManifests selection sorted) with
                         | Error reason->Refused reason
                         | Ok manifests->
                            let copies=sorted|>Array.map(fun row -> $"COPY --chown=32768:32768 --chmod={row.Mode} {JsonSerializer.Serialize([|row.SourcePath;row.TargetPath|])}")
                            let containerfile=String.concat "\n" (Array.concat[ [|$"FROM mcr.microsoft.com/dotnet/aspnet@{selection.RuntimeImageDigest}";"USER 32768:32768"|];copies;[|"ENTRYPOINT []";"CMD []"|] ])+"\n"
                            let native=manifests|>Array.find(fun manifest->manifest.Role="native")
                            let manager=manifests|>Array.find(fun manifest->manifest.Role="manager")
                            let verifierArgv=if selection.IdentityClass<>"synthetic-test" then [|"/opt/fsgg/python/bin/python3.14";"-I";"-S";"-B";"/opt/fsgg/verifier/learn_01_native_source.py";"verify"|] else [||]
                            let profile=JsonSerializer.Serialize({|schema="fsgg.telemetry.persistent-v3-image-profile/1";user=32768;group=32768;platform=selection.Platform;argv=native.Argv;managerArgv=manager.Argv;verifierArgv=verifierArgv;searchRoots=native.SearchRoots;loaderPaths=native.LoaderPaths;listeners=Array.empty<string>;serviceEnabled=false;activationAuthorized=false;readerProfileSha256=selection.ReaderProfileSha256|})
                            let trustedNativeSelection=if selection.IdentityClass="synthetic-test" then null else box trusted.Value
                            Prepared(JsonSerializer.SerializeToUtf8Bytes({|schema="fsgg.telemetry.persistent-v3-image-context/2";status=(if selection.IdentityClass="production" then "prepared-inactive" else "prepared-inactive-synthetic");identityClass=selection.IdentityClass;containerfile=containerfile;profile=profile;inventorySha256=selection.InventorySha256;runtimeImageDigest=selection.RuntimeImageDigest;trustedNativeSelection=trustedNativeSelection;roleManifestSha256=selection.RoleManifests|>Array.map(fun item->item.Role,item.Sha256)|>Map.ofArray;files=sorted|}))
    let prepare selection=prepareWithTrustedNative None selection
