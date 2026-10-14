module CallerSourceFixtures

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open FSGG.Telemetry.PersistentV3.ImageClosure

// Source fixture bytes only. They are never admitted controller/provider facts.
// Existing OciTests.withInactiveInputs owns package/OCI/receipt effect fixtures.
let withContractInputs exercise=
    let contractRoot=Directory.CreateTempSubdirectory("p2c3-caller-contract-").FullName
    let contractManifestRoot=Directory.CreateTempSubdirectory("p2c3-caller-authority-").FullName
    let readMode=UnixFileMode.UserRead|||UnixFileMode.GroupRead|||UnixFileMode.OtherRead
    let executeMode=readMode|||UnixFileMode.UserExecute|||UnixFileMode.GroupExecute|||UnixFileMode.OtherExecute
    let sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let h c=String(c,64)
    let inventory value=sha(JsonSerializer.SerializeToUtf8Bytes value)
    let write (root:string) (relative:string) (mode:UnixFileMode) (text:string)=
        let path=Path.Combine(root,relative)
        Directory.CreateDirectory(Path.GetDirectoryName path)|>ignore
        File.WriteAllText(path,text);File.SetUnixFileMode(path,mode);path
    File.SetUnixFileMode(contractRoot,enum<UnixFileMode> 0o700)
    File.SetUnixFileMode(contractManifestRoot,enum<UnixFileMode> 0o700)
    try
        let root=contractRoot
        let manifestRoot=contractManifestRoot
        let rows:FileRow array=[||]
        let manifests:RoleManifestReference array=[||]
        let selected={IdentityClass="synthetic-test";AcquisitionRoot=root;ManifestRoot=manifestRoot;RoleManifests=manifests;NativeInventoryPath="";NativeInventorySha256="";OwnerUid=ImageClosure.ownerUid root;Platform="linux/amd64";HostSourceRevision=h 'a';HostReleaseId=1L;HostRunId=2L;HostArchiveSha256=h 'b';HostPackageSha256=h 'c';HostPayloadSha256=h 'd';HostManifestSha256=h 'e';HostJournalSha256=h 'f';ManagerSourceRevision=String('a',40);ManagerSourceTree=String('b',40);ManagerArtifactSha256=h '1';ManagerManifestSha256=h '2';ManagerPreparedSha256=h '3';ManagerArchiveSha256=h '4';ManagerRunId=3L;ManagerArtifactId=4L;RuntimeImageDigest="sha256:"+h '5';RuntimeTreeSha256=h '6';NativeElfSha256=h '7';NativeProfileSha256=h '8';ReaderProfileSha256=h '9';CanonicalVerifierSha256=h 'a';InventorySha256=inventory rows;Files=rows}
        let contractRow source target sourceClass mode text =
          let path=write contractRoot source mode text
          {SourcePath=source;TargetPath=target;Bytes=FileInfo(path).Length;Sha256=sha(File.ReadAllBytes path);Mode=(if mode.HasFlag UnixFileMode.UserExecute then "0555" else "0444");SourceClass=sourceClass}
        let hostRow=contractRow "host/FS.GG.Telemetry.Host.0.4.0.nupkg" "/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.0.4.0.nupkg" "host" readMode "host-package"
        let managerRow=contractRow "telemetry-host-manager-net10.0/TelemetryHostManager.dll" "/opt/fsgg/telemetry-host-manager/TelemetryHostManager.dll" "manager" readMode "manager-payload"
        let managerDependencyRow=contractRow "telemetry-host-manager-net10.0/FSharp.Core.dll" "/opt/fsgg/telemetry-host-manager/FSharp.Core.dll" "manager" readMode "manager-dependency"
        let runtimeRow=contractRow "runtime/dotnet" "/usr/share/dotnet/dotnet" "runtime" executeMode "runtime-dotnet"
        let codexRow=contractRow "native/codex" "/opt/fsgg/codex/codex" "native" executeMode "native-elf"
        let configRow=contractRow "native/native-producer-config.toml" "/opt/fsgg/native-producer-config.toml" "native" readMode "config"
        let protocolRow=contractRow "native/native-protocol-schema.json" "/opt/fsgg/native-protocol-schema.json" "native" readMode "protocol"
        let flagsRow=contractRow "native/native-session-flags.json" "/opt/fsgg/native-session-flags.json" "native" readMode "flags"
        let verifierRow=contractRow "native/learn_01_native_source.py" "/opt/fsgg/verifier/learn_01_native_source.py" "native" readMode "verifier"
        let pythonRow=contractRow "native/python3.14" "/opt/fsgg/python/bin/python3.14" "native" executeMode "python"
        let stdlibRow=contractRow "native/os.py" "/opt/fsgg/python/lib/python3.14/os.py" "native" readMode "stdlib"
        let extensionRow=contractRow "native/_hashlib.so" "/opt/fsgg/python/lib/python3.14/lib-dynload/_hashlib.so" "native" readMode "extension"
        let loaderRow=contractRow "native/ld-linux-x86-64.so.2" "/lib64/ld-linux-x86-64.so.2" "native" readMode "loader"
        let libraryRow=contractRow "native/libc.so.6" "/usr/lib/x86_64-linux-gnu/libc.so.6" "native" readMode "library"
        let osDataRow=contractRow "native/ca-certificates.crt" "/etc/ssl/certs/ca-certificates.crt" "native" readMode "os-data"
        let readerRow=contractRow "native/reader-profile.json" "/opt/fsgg/profile/reader-profile.json" "native" readMode "reader"
        let nativeProfileObject={|schema="fsgg.telemetry.native-operation-profile/1";operationId="controlled";supportedOperations=[|"controlled"|];provider="controlled";model="controlled";effort="none";prompt="none";native={|executable="/opt/fsgg/codex/codex";sha256=codexRow.Sha256;bytes=codexRow.Bytes;version="test";config="/opt/fsgg/native-producer-config.toml";configSha256=configRow.Sha256;protocolSchemaSha256=protocolRow.Sha256;sessionFlagsSha256=flagsRow.Sha256|};producer={|controlled=true|};runtime={|home="/qualification/native";codexHome="/qualification/native/.codex";cwd="/qualification/native/work";timeoutSeconds=300;maximumLineBytes=1048576;maximumEvents=4096;python="3.14.0"|};network={|controlled=true|}|}
        let nativeProfileBytes=JsonSerializer.SerializeToUtf8Bytes nativeProfileObject
        let nativeProfilePath=Path.Combine(contractRoot,"native/native-operation-v1.json")
        File.WriteAllBytes(nativeProfilePath,nativeProfileBytes);File.SetUnixFileMode(nativeProfilePath,readMode)
        let nativeProfileRow={SourcePath="native/native-operation-v1.json";TargetPath="/opt/fsgg/profile/native-operation-v1.json";Bytes=FileInfo(nativeProfilePath).Length;Sha256=sha nativeProfileBytes;Mode="0444";SourceClass="native"}
        // The optional immutable root replays actual acquired Python image bytes; no file is dropped.
        let emptyInitializerRows =
          [|"compression/__init__.py";"compression/_common/__init__.py";"email/mime/__init__.py";"pydoc_data/__init__.py";"urllib/__init__.py"|]
          |>Array.map(fun relative->
              let immutableRoot=Environment.GetEnvironmentVariable "PERSISTENT_V3_IMMUTABLE_PYTHON_ROOT"
              if not(String.IsNullOrWhiteSpace immutableRoot) then
                let actual=File.ReadAllBytes(Path.Combine(immutableRoot,relative))
                if actual.Length<>0||sha actual<>sha [||] then raise(InvalidDataException "immutable Python initializer bytes differ")
              contractRow ("native/python/"+relative) ("/opt/fsgg/python/lib/python3.14/"+relative) "native" readMode "")
        let contractRows=Array.append emptyInitializerRows [|hostRow;managerRow;managerDependencyRow;runtimeRow;codexRow;configRow;protocolRow;flagsRow;verifierRow;pythonRow;stdlibRow;extensionRow;loaderRow;libraryRow;osDataRow;readerRow;nativeProfileRow|]|>Array.sortBy _.TargetPath
        let authorityFile name (bytes:byte array) =
          let path=Path.Combine(contractManifestRoot,name)
          File.WriteAllBytes(path,bytes);File.SetUnixFileMode(path,readMode)
          sha bytes
        let hostPayload=h 'd'
        let hostManifestBytes=JsonSerializer.SerializeToUtf8Bytes({|archiveSha256=hostRow.Sha256;createdAt="2026-10-02T00:00:00Z";dependencyLockSha256=h 'a';framework="net10.0";packageId="FS.GG.Telemetry.Host";producerPayloadSha256="sha256:"+hostPayload;runtimePrerequisites=[|"Microsoft.AspNetCore.App 10.0";"Microsoft.NETCore.App 10.0"|];schema="fsgg.telemetry.host-release/1";sourceSha=String('a',40);supportedStoreSchemaMax=12;supportedStoreSchemaMin=10;tag="telemetry-host/v0.4.0";target="linux-x64";uiAssetTreeSha256=h 'b';version="0.4.0"|})
        let hostManifestSha=authorityFile "host.json" hostManifestBytes
        let runtimeItem={|bytes=runtimeRow.Bytes;mode=runtimeRow.Mode;path="dotnet";sha256=runtimeRow.Sha256|}
        let runtimeManifestBytes=JsonSerializer.SerializeToUtf8Bytes [|runtimeItem|]
        let runtimeManifestSha=authorityFile "runtime.json" runtimeManifestBytes
        let managerItem={|bytes=managerRow.Bytes;mode=managerRow.Mode;path="telemetry-host-manager-net10.0/TelemetryHostManager.dll";sha256=managerRow.Sha256|}
        let managerDependencyItem={|bytes=managerDependencyRow.Bytes;mode=managerDependencyRow.Mode;path="telemetry-host-manager-net10.0/FSharp.Core.dll";sha256=managerDependencyRow.Sha256|}
        let managerManifestBytes=JsonSerializer.SerializeToUtf8Bytes({|schema="fsgg.coordination.telemetry-host-manager-bundle/2";archiveRoot="telemetry-host-manager-net10.0";buildSdk={|controlled=true|};entrypoint="telemetry-host-manager-net10.0/TelemetryHostManager.dll";installationRoot="/opt/fsgg/telemetry-host-manager";fixedArgv=[|"/usr/share/dotnet/dotnet";"exec";"--fx-version";"10.0.12";"/opt/fsgg/telemetry-host-manager/TelemetryHostManager.dll"|];payloads=[|managerDependencyItem;managerItem|];source={|lockFile={|path="eng/telemetry-host-manager/packages.lock.json";sha256=h 'a'|};project={|path="eng/telemetry-host-manager/TelemetryHostManager.fsproj";sha256=h 'b'|};repository="FS-GG/FS.GG.Coordination";revision=String('b',40);tree=String('c',40)|};runtime={|aspnetFramework="Microsoft.AspNetCore.App";aspnetFrameworkVersion="10.0.12";dotnetRoot="/usr/share/dotnet";files=[|runtimeItem|];framework="Microsoft.NETCore.App";frameworkVersion="10.0.12";rollForward="Disable";source={|manifestDigest="sha256:"+h '5'|};treeSha256=runtimeManifestSha|}|})
        let managerManifestSha=authorityFile "manager.json" managerManifestBytes
        let nativeManifestSha=authorityFile "native.json" nativeProfileBytes
        let nativeAuthorityFiles=contractRows|>Array.filter(fun row->row.SourceClass="native")|>Array.map(fun row->{SourcePath=row.SourcePath;TargetPath=row.TargetPath;Bytes=row.Bytes;Sha256=row.Sha256;Mode=row.Mode})
        let acquisitionProvenanceSha=h '7'
        let nativeInventory={Schema="fsgg.telemetry.native-acquisition-inventory/1";AcquisitionProvenanceSha256=acquisitionProvenanceSha;ProfileSha256=nativeManifestSha;ReaderProfileSha256=readerRow.Sha256;NativeExecutable=codexRow.TargetPath;NativeExecutableSha256=codexRow.Sha256;PythonExecutable=pythonRow.TargetPath;PythonVersion="3.14.0";VerifierArgv=[|pythonRow.TargetPath;"-I";"-S";"-B";verifierRow.TargetPath;"verify"|];SearchRoots=[|"/opt/fsgg/python/lib/python3.14"|];LoaderPath=loaderRow.TargetPath;ImportPaths=Array.append (emptyInitializerRows|>Array.map _.TargetPath) [|extensionRow.TargetPath;stdlibRow.TargetPath|]|>Array.sort;LibraryPaths=[|libraryRow.TargetPath|];OsDataPaths=[|osDataRow.TargetPath|];Files=nativeAuthorityFiles}
        let nativeInventoryBytes=JsonSerializer.SerializeToUtf8Bytes nativeInventory
        let nativeInventorySha=authorityFile "native-inventory.json" nativeInventoryBytes
        let trustedNative={Schema="fsgg.telemetry.trusted-native-selection/1";AcquisitionProvenanceSha256=acquisitionProvenanceSha;NativeInventorySha256=nativeInventorySha;ProfileSha256=nativeManifestSha;ReaderProfileSha256=readerRow.Sha256;NativeExecutableSha256=codexRow.Sha256;PythonExecutable=pythonRow.TargetPath;PythonVersion="3.14.0";VerifierArgv=nativeInventory.VerifierArgv;SearchRoots=nativeInventory.SearchRoots;LoaderPath=loaderRow.TargetPath;ImportPaths=nativeInventory.ImportPaths;LibraryPaths=nativeInventory.LibraryPaths;OsDataPaths=nativeInventory.OsDataPaths}
        let contractRefs=[|{Role="host";Path="host.json";Sha256=hostManifestSha};{Role="manager";Path="manager.json";Sha256=managerManifestSha};{Role="native";Path="native.json";Sha256=nativeManifestSha};{Role="runtime";Path="runtime.json";Sha256=runtimeManifestSha}|]
        let contractSelection={selected with IdentityClass="production-contract-test";AcquisitionRoot=contractRoot;ManifestRoot=contractManifestRoot;RoleManifests=contractRefs;NativeInventoryPath="native-inventory.json";NativeInventorySha256=nativeInventorySha;HostSourceRevision=String('a',40);HostArchiveSha256=hostRow.Sha256;HostPayloadSha256=hostPayload;HostManifestSha256=hostManifestSha;ManagerSourceRevision=String('b',40);ManagerSourceTree=String('c',40);ManagerManifestSha256=managerManifestSha;RuntimeImageDigest="sha256:"+h '5';RuntimeTreeSha256=runtimeManifestSha;NativeElfSha256=codexRow.Sha256;NativeProfileSha256=nativeManifestSha;ReaderProfileSha256=readerRow.Sha256;CanonicalVerifierSha256=verifierRow.Sha256;InventorySha256=inventory contractRows;Files=contractRows}
        exercise contractSelection trustedNative
    finally
        Directory.Delete(contractRoot,true)
        Directory.Delete(contractManifestRoot,true)
