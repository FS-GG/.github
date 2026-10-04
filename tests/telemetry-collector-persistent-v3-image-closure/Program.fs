open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FSGG.Telemetry.PersistentV3.ImageClosure

// Source-only targeted route exits before fixture creation or process-backed checks.
PathControls.run()
if Environment.GetEnvironmentVariable "PERSISTENT_V3_PATH_CONTROLS_ONLY" = "1" then
    Environment.Exit 0

type DelegateMechanism(handler:Effect*CancellationToken->Task<Observation>)=
  interface IRunnerMechanism with member _.Execute(effect,token)=handler(effect,token)

let fail message=raise(InvalidOperationException message)
let sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
let write (root:string) (relative:string) (mode:UnixFileMode) (text:string) =
  let path=Path.Combine(root,relative)
  Directory.CreateDirectory(Path.GetDirectoryName path)|>ignore
  File.WriteAllText(path,text)
  File.SetUnixFileMode(path,mode)
  path
let root=Directory.CreateTempSubdirectory("p2c3-physical-").FullName
File.SetUnixFileMode(root,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let readMode=UnixFileMode.UserRead|||UnixFileMode.GroupRead|||UnixFileMode.OtherRead
let executeMode=readMode|||UnixFileMode.UserExecute|||UnixFileMode.GroupExecute|||UnixFileMode.OtherExecute
let definitions=["host/host.dll","host",readMode,"host-actual";"manager/manager.dll","manager",readMode,"manager-actual";"native/codex","native",executeMode,"native-actual";"runtime/verify.py","runtime",readMode,"verifier-actual"]
let rows=
  definitions
  |>List.map(fun(relative,sourceClass,mode,text)->
      let path=write root relative mode text
      {SourcePath=relative;TargetPath="/opt/fsgg/image/"+relative;Bytes=FileInfo(path).Length;Sha256=sha(File.ReadAllBytes path);Mode=(if mode.HasFlag UnixFileMode.UserExecute then "0555" else "0444");SourceClass=sourceClass})
  |>List.toArray
let inventory value=sha(JsonSerializer.SerializeToUtf8Bytes value)
let h c=String(c,64)
let manifestRoot=Directory.CreateTempSubdirectory("p2c3-manifests-").FullName
File.SetUnixFileMode(manifestRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let authority role=match role with "host"->h 'e'|"manager"->h '2'|"runtime"->h '6'|_->h '8'
let roleManifest role =
  let files=rows|>Array.filter(fun row->row.SourceClass=role)|>Array.map(fun row->{SourcePath=row.SourcePath;TargetPath=row.TargetPath;Bytes=row.Bytes;Sha256=row.Sha256;Mode=row.Mode})
  {Schema="fsgg.telemetry.persistent-v3-role-closure/1";Role=role;AuthoritySha256=authority role;SearchRoots=[|"/opt/fsgg/image/"+role|];Argv=(if role="native" then [|"/opt/fsgg/image/native/codex"|] else [||]);LoaderPaths=(if role="native" then [|"/opt/fsgg/image/runtime/verify.py"|] else [||]);Files=files}
let writeManifest name (manifest:RoleManifest) : RoleManifestReference =
  let bytes=JsonSerializer.SerializeToUtf8Bytes manifest
  let path=Path.Combine(manifestRoot,name+".json")
  File.WriteAllBytes(path,bytes)
  File.SetUnixFileMode(path,readMode)
  {Role=manifest.Role;Path=name+".json";Sha256=sha bytes}
let manifests=[|"host";"manager";"native";"runtime"|]|>Array.map(fun role->writeManifest role (roleManifest role))
let selected={IdentityClass="synthetic-test";AcquisitionRoot=root;ManifestRoot=manifestRoot;RoleManifests=manifests;NativeInventoryPath="";NativeInventorySha256="";OwnerUid=ImageClosure.ownerUid root;Platform="linux/amd64";HostSourceRevision=h 'a';HostReleaseId=1L;HostRunId=2L;HostArchiveSha256=h 'b';HostPackageSha256=h 'c';HostPayloadSha256=h 'd';HostManifestSha256=h 'e';HostJournalSha256=h 'f';ManagerSourceRevision=String('a',40);ManagerSourceTree=String('b',40);ManagerArtifactSha256=h '1';ManagerManifestSha256=h '2';ManagerPreparedSha256=h '3';ManagerArchiveSha256=h '4';ManagerRunId=3L;ManagerArtifactId=4L;RuntimeImageDigest="sha256:"+h '5';RuntimeTreeSha256=h '6';NativeElfSha256=h '7';NativeProfileSha256=h '8';ReaderProfileSha256=h '9';CanonicalVerifierSha256=h 'a';InventorySha256=inventory rows;Files=rows}
// Execute the actual CLI: canonical inert bytes stay unavailable; even one changed byte refuses.
let cliRoot=Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../deployment/telemetry-collector/persistent/v3/image-closure"))
let cli=Path.Combine(cliRoot,"bin/Debug/net10.0/PersistentV3.ImageClosure.dll")
let invokePlaceholder path =
  let start=System.Diagnostics.ProcessStartInfo("dotnet")
  start.UseShellExecute<-false
  start.RedirectStandardOutput<-true
  start.RedirectStandardError<-true
  start.ArgumentList.Add cli
  start.ArgumentList.Add path
  use child=System.Diagnostics.Process.Start start
  let stdout=child.StandardOutput.ReadToEnd()
  let stderr=child.StandardError.ReadToEnd().Trim()
  child.WaitForExit()
  child.ExitCode,stdout,stderr
let canonicalPlaceholder=Path.Combine(cliRoot,"production-selection.json")
match invokePlaceholder canonicalPlaceholder with
| 2,"","persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required"->()
| actual->fail $"actual canonical placeholder outcome differs: %A{actual}"
let changedPlaceholder=Path.Combine(root,"changed-production-selection.json")
let canonicalPlaceholderBytes=File.ReadAllBytes canonicalPlaceholder
File.WriteAllBytes(changedPlaceholder,Array.append canonicalPlaceholderBytes [|byte ' '|])
match invokePlaceholder changedPlaceholder with
| 3,"","persistent-v3-image-closure-refused: acquisition-required-selection-bytes-differ"->()
| actual->fail $"actual changed-byte placeholder outcome differs: %A{actual}"
File.Delete changedPlaceholder
printfn "PASS actual canonical placeholder unavailability and changed-byte refusal"
let prepared:byte array=match ImageClosure.prepare selected with ClosureResult.Prepared bytes->bytes|value->fail $"physical positive refused: %A{value}"
let production={selected with IdentityClass="production";OwnerUid=0;HostSourceRevision=ImageClosure.HostSourceRevision;HostReleaseId=401538149L;HostRunId=36964135216L;HostArchiveSha256=ImageClosure.HostArchiveSha256;HostPackageSha256=ImageClosure.HostPackageSha256;HostPayloadSha256=ImageClosure.HostPayloadSha256;HostManifestSha256=ImageClosure.HostManifestSha256;HostJournalSha256=ImageClosure.HostJournalSha256;ManagerSourceRevision=ImageClosure.ManagerSourceRevision;ManagerSourceTree=ImageClosure.ManagerSourceTree;ManagerArtifactSha256=ImageClosure.ManagerArtifactSha256;ManagerManifestSha256=ImageClosure.ManagerManifestSha256;ManagerPreparedSha256=ImageClosure.ManagerPreparedSha256;ManagerArchiveSha256=ImageClosure.ManagerArchiveSha256;ManagerRunId=36983338783L;ManagerArtifactId=11216418410L;RuntimeImageDigest=ImageClosure.RuntimeImageDigest;RuntimeTreeSha256=ImageClosure.RuntimeTreeSha256;NativeElfSha256=ImageClosure.NativeElfSha256;NativeProfileSha256=ImageClosure.NativeProfileSha256;CanonicalVerifierSha256=ImageClosure.CanonicalVerifierSha256}
match ImageClosure.prepare production with ClosureResult.Unavailable "trusted-native-selection-acquisition-required"->()|value->fail $"unacquired production closure did not require trusted root selection: %A{value}"
let contractRoot=Directory.CreateTempSubdirectory("p2c3-production-contract-").FullName
let contractManifestRoot=Directory.CreateTempSubdirectory("p2c3-production-authority-").FullName
File.SetUnixFileMode(contractRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
File.SetUnixFileMode(contractManifestRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
let contractRow source target sourceClass mode text =
  let path=write contractRoot source mode text
  {SourcePath=source;TargetPath=target;Bytes=FileInfo(path).Length;Sha256=sha(File.ReadAllBytes path);Mode=(if mode.HasFlag UnixFileMode.UserExecute then "0555" else "0444");SourceClass=sourceClass}
let hostRow=contractRow "host/FS.GG.Telemetry.Host.0.3.0.nupkg" "/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.0.3.0.nupkg" "host" readMode "host-package"
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
        if actual.Length<>0||sha actual<>sha [||] then fail "immutable Python initializer bytes differ"
      contractRow ("native/python/"+relative) ("/opt/fsgg/python/lib/python3.14/"+relative) "native" readMode "")
let contractRows=Array.append emptyInitializerRows [|hostRow;managerRow;managerDependencyRow;runtimeRow;codexRow;configRow;protocolRow;flagsRow;verifierRow;pythonRow;stdlibRow;extensionRow;loaderRow;libraryRow;osDataRow;readerRow;nativeProfileRow|]|>Array.sortBy _.TargetPath
let authorityFile name (bytes:byte array) =
  let path=Path.Combine(contractManifestRoot,name)
  File.WriteAllBytes(path,bytes);File.SetUnixFileMode(path,readMode)
  sha bytes
let hostPayload=h 'd'
let hostManifestBytes=JsonSerializer.SerializeToUtf8Bytes({|archiveSha256=hostRow.Sha256;createdAt="2026-10-02T00:00:00Z";dependencyLockSha256=h 'a';framework="net10.0";packageId="FS.GG.Telemetry.Host";producerPayloadSha256="sha256:"+hostPayload;runtimePrerequisites=[|"Microsoft.AspNetCore.App 10.0";"Microsoft.NETCore.App 10.0"|];schema="fsgg.telemetry.host-release/1";sourceSha=String('a',40);supportedStoreSchemaMax=12;supportedStoreSchemaMin=10;tag="telemetry-host/v0.3.0";target="linux-x64";uiAssetTreeSha256=h 'b';version="0.3.0"|})
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
let prepareContract value=ImageClosure.prepareWithTrustedNative (Some trustedNative) value
OciTests.run selected prepared trustedNative
OciTests.processBoundaries()
let contractPrepared=match prepareContract contractSelection with ClosureResult.Prepared bytes->JsonDocument.Parse bytes|value->fail $"faithful production contract fixture refused: %A{value}"
let contractProfile=contractPrepared.RootElement.GetProperty("profile").GetString()
if not(contractProfile.Contains("\\u0022managerArgv\\u0022"))&&not(contractProfile.Contains("\"managerArgv\""))||not(contractProfile.Contains("-I"))||not(contractProfile.Contains("/opt/fsgg/python/bin/python3.14")) then fail "production argv/search layout absent"
let preparedTrust=contractPrepared.RootElement.GetProperty("trustedNativeSelection")
if preparedTrust.GetProperty("NativeInventorySha256").GetString()<>nativeInventorySha||preparedTrust.GetProperty("AcquisitionProvenanceSha256").GetString()<>acquisitionProvenanceSha then fail "prepared context omitted trusted native selection"
let expectContractRefusal expected value=match prepareContract value with ClosureResult.Refused reason when reason=expected->()|actual->fail $"production contract did not refuse at {expected}: %A{actual}"
let refuseRow (replacement:FileRow) =
  let changed=contractRows|>Array.map(fun row->if row.SourcePath=replacement.SourcePath then replacement else row)
  expectContractRefusal "inventory-entry" {contractSelection with Files=changed;InventorySha256=inventory changed}
refuseRow {emptyInitializerRows[0] with Sha256=h 'f'}
refuseRow {emptyInitializerRows[0] with Sha256="malformed"}
refuseRow {emptyInitializerRows[0] with Bytes= -1L}
refuseRow {emptyInitializerRows[0] with Mode="0555"}
for row in [|hostRow;managerRow;runtimeRow;codexRow;pythonRow|] do
  refuseRow {row with Bytes=0L;Sha256=sha [||]}
printfn "PASS exact empty Python initializers and positive payload guards"
match ImageClosure.prepare contractSelection with ClosureResult.Unavailable "trusted-native-selection-acquisition-required"->()|actual->fail $"missing trusted selection was not unavailable: %A{actual}"
match prepareContract {contractSelection with NativeInventoryPath="";NativeInventorySha256=""} with ClosureResult.Unavailable "native-authority-inventory-acquisition-required"->()|actual->fail $"missing native authority was not unavailable: %A{actual}"
expectContractRefusal "native-authority-inventory-selection-mismatch" {contractSelection with NativeInventorySha256=h 'f'}
let expectTrustedRefusal expected trusted value=match ImageClosure.prepareWithTrustedNative (Some trusted) value with ClosureResult.Refused reason when reason=expected->()|actual->fail $"trusted production contract did not refuse at {expected}: %A{actual}"
expectTrustedRefusal "production-native-authority-inventory-mismatch" {trustedNative with ProfileSha256=h '8'} contractSelection
expectTrustedRefusal "production-native-authority-inventory-mismatch" {trustedNative with AcquisitionProvenanceSha256=h '8'} contractSelection
expectTrustedRefusal "production-native-authority-inventory-mismatch" {trustedNative with ImportPaths=[|"/opt/fsgg/python/lib/python3.14/decoy.py";extensionRow.TargetPath|]|>Array.sort} contractSelection
File.Delete(Path.Combine(contractRoot,managerRow.SourcePath))
let withoutManager=contractRows|>Array.filter(fun row->row.SourcePath<>managerRow.SourcePath)
expectContractRefusal "production-authority-inventory-mismatch" {contractSelection with Files=withoutManager;InventorySha256=inventory withoutManager}
write contractRoot managerRow.SourcePath readMode "manager-payload"|>ignore
File.Delete(Path.Combine(contractRoot,stdlibRow.SourcePath))
let withoutStdlib=contractRows|>Array.filter(fun row->row.SourcePath<>stdlibRow.SourcePath)
expectContractRefusal "production-native-authority-inventory-mismatch" {contractSelection with Files=withoutStdlib;InventorySha256=inventory withoutStdlib}
write contractRoot stdlibRow.SourcePath readMode "stdlib"|>ignore
let redirectedDependencies=contractRows|>Array.map(fun row->if row.SourcePath=stdlibRow.SourcePath then {row with TargetPath="/opt/fsgg/python/lib/python3.14/decoy.py"} elif row.SourcePath=extensionRow.SourcePath then {row with TargetPath="/opt/fsgg/python/lib/python3.14/lib-dynload/decoy.so"} else row)|>Array.sortBy _.TargetPath
expectContractRefusal "production-native-authority-inventory-mismatch" {contractSelection with Files=redirectedDependencies;InventorySha256=inventory redirectedDependencies}
let redirect target=if target=stdlibRow.TargetPath then "/opt/fsgg/python/lib/python3.14/decoy.py" elif target=extensionRow.TargetPath then "/opt/fsgg/python/lib/python3.14/lib-dynload/decoy.so" else target
let replacementAuthority={nativeInventory with ImportPaths=nativeInventory.ImportPaths|>Array.map redirect|>Array.sort;Files=nativeInventory.Files|>Array.map(fun row->{row with TargetPath=redirect row.TargetPath})|>Array.sortBy _.TargetPath}
let replacementAuthoritySha=authorityFile "replacement-native-inventory.json" (JsonSerializer.SerializeToUtf8Bytes replacementAuthority)
expectContractRefusal "native-authority-inventory-selection-mismatch" {contractSelection with Files=redirectedDependencies;InventorySha256=inventory redirectedDependencies;NativeInventoryPath="replacement-native-inventory.json";NativeInventorySha256=replacementAuthoritySha}
File.Delete(Path.Combine(contractRoot,libraryRow.SourcePath))
let withoutLibrary=contractRows|>Array.filter(fun row->row.SourcePath<>libraryRow.SourcePath)
expectContractRefusal "production-native-authority-inventory-mismatch" {contractSelection with Files=withoutLibrary;InventorySha256=inventory withoutLibrary}
write contractRoot libraryRow.SourcePath readMode "library"|>ignore
let wrongHostBytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(hostManifestBytes).Replace("fsgg.telemetry.host-release/1","wrong"))
let wrongHostSha=authorityFile "host-wrong.json" wrongHostBytes
let wrongHostRefs=contractRefs|>Array.map(fun item->if item.Role="host" then {item with Path="host-wrong.json";Sha256=wrongHostSha} else item)
expectContractRefusal "production-host-authority" {contractSelection with HostManifestSha256=wrongHostSha;RoleManifests=wrongHostRefs}
let wrongManagerBytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(managerManifestBytes).Replace("\"dotnetRoot\":\"/usr/share/dotnet\"","\"dotnetRoot\":\"/wrong/runtime\""))
let wrongManagerSha=authorityFile "manager-wrong.json" wrongManagerBytes
let wrongManagerRefs=contractRefs|>Array.map(fun item->if item.Role="manager" then {item with Path="manager-wrong.json";Sha256=wrongManagerSha} else item)
expectContractRefusal "production-runtime-authority" {contractSelection with ManagerManifestSha256=wrongManagerSha;RoleManifests=wrongManagerRefs}
let context=JsonDocument.Parse prepared
let containerfile=context.RootElement.GetProperty("containerfile").GetString()
if context.RootElement.GetProperty("schema").GetString()<>"fsgg.telemetry.persistent-v3-image-context/2"||not(containerfile.StartsWith("FROM mcr.microsoft.com/dotnet/aspnet@sha256:"))||not(containerfile.Contains("COPY --chown=32768:32768 --chmod=0444 [\"host/host.dll\",\"/opt/fsgg/image/host/host.dll\"]"))||not(containerfile.Contains("ENTRYPOINT []"))||not(context.RootElement.GetProperty("profile").GetString().Contains("\"group\":32768")) then fail "concrete context/profile absent"
let refuses name expected mutate=match ImageClosure.prepare(mutate selected)with ClosureResult.Refused actual when actual=expected->()|value->fail $"{name} did not refuse at {expected}: %A{value}"
let withRows next value={value with Files=next;InventorySha256=inventory next}
refuses "nonexistent" "acquisition-root-inventory-incomplete" (withRows [|{rows[0] with SourcePath="host/nonexistent.dll"};rows[1];rows[2];rows[3]|])
refuses "path" "inventory-entry" (withRows [|{rows[0] with SourcePath="../escape"};rows[1];rows[2];rows[3]|])
refuses "mode" "inventory-physical-mismatch" (withRows [|{rows[0] with Mode="0555"};rows[1];rows[2];rows[3]|])
refuses "bytes" "inventory-physical-mismatch" (withRows [|{rows[0] with Bytes=rows[0].Bytes+1L};rows[1];rows[2];rows[3]|])
refuses "hash" "inventory-physical-mismatch" (withRows [|{rows[0] with Sha256=h 'd'};rows[1];rows[2];rows[3]|])
refuses "omission" "inventory-omits-dependency-root" (withRows [|rows[0];rows[1];rows[2]|])
refuses "duplicate" "inventory-order-or-duplicate" (withRows [|rows[0];rows[0];rows[2];rows[3]|])
refuses "duplicate source alias" "inventory-order-or-duplicate" (withRows [|rows[0];{rows[1] with SourcePath=rows[0].SourcePath};rows[2];rows[3]|])
refuses "source whitespace" "inventory-entry" (withRows [|{rows[0] with SourcePath="host/host file.dll"};rows[1];rows[2];rows[3]|])
refuses "target newline" "inventory-entry" (withRows [|{rows[0] with TargetPath="/opt/fsgg/image/host/host.dll\nRUN false"};rows[1];rows[2];rows[3]|])
refuses "platform" "selection-shape" (fun value->{value with Platform="linux/arm64"})
refuses "base identity" "fixed-production-identity-mismatch" (fun value->{value with IdentityClass="production";OwnerUid=0;RuntimeImageDigest="sha256:"+h '0'})
refuses "role manifest omission" "role-manifest-set" (fun value->{value with RoleManifests=manifests[0..2]})
refuses "role manifest digest" "role-manifest-digest" (fun value->{value with RoleManifests=manifests|>Array.map(fun item->if item.Role="host" then {item with Sha256=h 'd'} else item)})
let badNative=writeManifest "native-bad" {roleManifest "native" with Argv=[||]}
refuses "native argv layout" "native-layout" (fun value->{value with RoleManifests=manifests|>Array.map(fun item->if item.Role="native" then badNative else item)})
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead|||UnixFileMode.UserWrite)
File.WriteAllText(Path.Combine(root,rows[0].SourcePath),"mutated-after-validation")
File.SetUnixFileMode(Path.Combine(root,rows[0].SourcePath),UnixFileMode.UserRead)
refuses "input mutation" "inventory-physical-mismatch" id
File.Delete(Path.Combine(root,rows[0].SourcePath))
File.CreateSymbolicLink(Path.Combine(root,rows[0].SourcePath),"../runtime/verify.py")|>ignore
refuses "symbolic link" "acquisition-census-link" (withRows [|{rows[0] with Bytes=rows[3].Bytes;Sha256=rows[3].Sha256};rows[1];rows[2];rows[3]|])

let runRoot=Directory.CreateTempSubdirectory("p2c3-runner-").FullName
let final,trace=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 10.0) CancellationToken.None (RunnerExecution.DirectoryFixture(runRoot,"input-1","image-x","image-x")) (Runner.initial "input-1")
if not(Runner.qualificationAccepted final)||trace.Length<>13||trace|>List.map(fst>>function CreateStore _->"create"|StartBuild _->"start"|AwaitBuild _->"await"|CancelBuild _->"cancel"|ValidateInputs _->"validate"|AcquireInputs _->"acquire"|CompareBuilds _->"compare"|QualifyInactive _->"qualify"|RemoveStore _->"remove")<>["acquire";"validate";"create";"create";"start";"await";"start";"await";"compare";"validate";"qualify";"remove";"remove"] then fail "typed runner command order/terminal differs"
let lateRoot=Directory.CreateTempSubdirectory("p2c3-late-final-").FullName
let lateBase=RunnerExecution.DirectoryFixture(lateRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let lateFinalRemove=DelegateMechanism(fun(effect,token)->match effect with RemoveStore resource when resource.EndsWith("store-b",StringComparison.Ordinal)->Thread.Sleep 600;Task.FromResult(StoreAbsent resource)|_->lateBase.Execute(effect,token))
let lateClock=Diagnostics.Stopwatch.StartNew()
let lateFinal,_=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddMilliseconds 150.0) CancellationToken.None lateFinalRemove (Runner.initial "input-1")
lateClock.Stop()
if Runner.qualificationAccepted lateFinal||lateFinal.Phase=Complete||lateClock.ElapsedMilliseconds>500L then fail "synchronous final cleanup escaped the outer deadline boundary"
Thread.Sleep 550
let cancelledFinalRoot=Directory.CreateTempSubdirectory("p2c3-cancel-final-").FullName
let cancelledFinalBase=RunnerExecution.DirectoryFixture(cancelledFinalRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let finalCancellation=new CancellationTokenSource()
let cancellationDuringFinal=DelegateMechanism(fun(effect,token)->match effect with RemoveStore resource when resource.EndsWith("store-b",StringComparison.Ordinal)->finalCancellation.Cancel();Task.FromResult(StoreAbsent resource)|_->cancelledFinalBase.Execute(effect,token))
let cancelledDuringFinal,_=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 5.0) finalCancellation.Token cancellationDuringFinal (Runner.initial "input-1")
if Runner.qualificationAccepted cancelledDuringFinal||cancelledDuringFinal.Phase=Complete||not cancelledDuringFinal.Cancelled then fail "cancellation during final cleanup was accepted"
let exceptionRoot=Directory.CreateTempSubdirectory("p2c3-exception-").FullName
let baseException=RunnerExecution.DirectoryFixture(exceptionRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let startThrows=DelegateMechanism(fun(effect,token)->match effect with StartBuild _->Task.FromException<Observation>(InvalidOperationException "start-failed")|_->baseException.Execute(effect,token))
let exceptionFinal,exceptionTrace=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 5.0) CancellationToken.None startThrows (Runner.initial "input-1")
if exceptionFinal.Phase<>Indeterminate||not exceptionFinal.Owned.IsEmpty||exceptionFinal.UnknownBuilds<>set["a"]||(Directory.EnumerateFileSystemEntries(exceptionRoot)|>Seq.isEmpty|>not)||not(exceptionTrace|>List.exists(fun(_,observation)->observation=BuildMayHaveEffect "a")) then fail "mechanism exception did not preserve uncertainty and clean known stores"
let blockedRoot=Directory.CreateTempSubdirectory("p2c3-blocked-").FullName
let baseBlocked=RunnerExecution.DirectoryFixture(blockedRoot,"input-1","image-x","image-x") :> IRunnerMechanism
let never=TaskCompletionSource<Observation>()
let blocked=DelegateMechanism(fun(effect,token)->match effect with StartBuild _->never.Task|_->baseBlocked.Execute(effect,token))
let blockedFinal,_=RunnerExecution.run 32 (DateTimeOffset.UtcNow.AddSeconds 3.0) CancellationToken.None blocked (Runner.initial "input-1")
if blockedFinal.Phase<>Indeterminate||not blockedFinal.Owned.IsEmpty||(Directory.EnumerateFileSystemEntries(blockedRoot)|>Seq.isEmpty|>not) then fail "bounded blocked effect did not clean known stores"
let duplicateA={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a","same-id"];Pending=Some(CreateStore "b")}
let duplicate=Runner.observe(StoreCreated("b","same-id"))duplicateA
if duplicate.Phase<>Cleanup||duplicate.MayHaveEffect<>set["b"] then fail "duplicate store identity was admitted"
let duplicateProcess={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a","store-a";"b","store-b"];Pending=Some(StartBuild("a","input-1"))}
let processAlias=Runner.observe(BuildStarted("a","store-a"))duplicateProcess
if processAlias.Phase<>Cleanup||processAlias.UnknownBuilds<>set["a"] then fail "store/process identity alias was admitted"
let cleanupRoot=Directory.CreateTempSubdirectory("p2c3-cleanup-").FullName
let ownedPath=Path.Combine(cleanupRoot,"owned-a")
Directory.CreateDirectory ownedPath|>ignore
let cleanupState={Runner.initial "input-1" with Phase=Cleanup;Owned=Map.ofList["a",ownedPath];Cancelled=true}
let wrongAbsent=DelegateMechanism(fun(effect,_)->match effect with RemoveStore _->Task.FromResult(StoreAbsent "wrong-id")|_->Task.FromResult(EffectFailed "unexpected"))
let wrongFinal,_=RunnerExecution.run 3 (DateTimeOffset.UtcNow.AddSeconds 2.0) CancellationToken.None wrongAbsent cleanupState
if wrongFinal.Owned.IsEmpty||wrongFinal.Phase<>Indeterminate then fail "wrong absence identity removed ownership or hid bounded exhaustion"
let cleanupThrows=DelegateMechanism(fun(effect,_)->match effect with RemoveStore _->Task.FromException<Observation>(IOException "cleanup-failed")|_->Task.FromResult(EffectFailed "unexpected"))
let cleanupFinal,_=RunnerExecution.run 3 (DateTimeOffset.UtcNow.AddSeconds 2.0) CancellationToken.None cleanupThrows cleanupState
if not cleanupFinal.CleanupFailed||cleanupFinal.Owned.IsEmpty then fail "cleanup exception was not sticky"
let cancelRoot=Directory.CreateTempSubdirectory("p2c3-cancel-").FullName
let cancelA=Path.Combine(cancelRoot,"a")
let cancelB=Path.Combine(cancelRoot,"b")
Directory.CreateDirectory cancelA|>ignore;Directory.CreateDirectory cancelB|>ignore
let cancelState={Runner.initial "input-1" with Phase=CreatingStores;Owned=Map.ofList["a",cancelA;"b",cancelB]}
let cancelledToken=new CancellationTokenSource()
cancelledToken.Cancel()
let cancelFinal,_=RunnerExecution.run 8 (DateTimeOffset.UtcNow.AddSeconds 2.0) cancelledToken.Token (RunnerExecution.DirectoryFixture(cancelRoot,"input-1","x","x")) cancelState
if cancelFinal.Phase<>Refused||not cancelFinal.Owned.IsEmpty||(Directory.EnumerateFileSystemEntries(cancelRoot)|>Seq.isEmpty|>not) then fail "external cancellation did not clean owned stores"
let deadlineRoot=Directory.CreateTempSubdirectory("p2c3-deadline-").FullName
let deadlineStore=Path.Combine(deadlineRoot,"owned")
Directory.CreateDirectory deadlineStore|>ignore
let deadlineState={Runner.initial "input-1" with Phase=Cleanup;Owned=Map.ofList["a",deadlineStore]}
let deadlineFinal,_=RunnerExecution.run 8 (DateTimeOffset.UtcNow.AddMilliseconds -1.0) CancellationToken.None (RunnerExecution.DirectoryFixture(deadlineRoot,"input-1","x","x")) deadlineState
if deadlineFinal.Phase<>Indeterminate||deadlineFinal.Refusal<>Some "Deadline"||deadlineFinal.Owned.IsEmpty||not(Directory.Exists deadlineStore) then fail "absolute deadline did not stop effects with truthful retained ownership"
let acquired=Runner.initial "input-1"|>Runner.nextEffect|>fun(state,_)->Runner.observe(InputsAcquired "input-1")state|>Runner.nextEffect|>fun(state,_)->Runner.observe(InputsValidated "input-1")state
let storeA,_=Runner.nextEffect acquired
let withA=Runner.observe(StoreCreated("a","owned-a")) storeA
let storeB,_=Runner.nextEffect withA
let withStores=Runner.observe(StoreCreated("b","owned-b")) storeB
let requested,_=Runner.nextEffect withStores
let lost=Runner.observe(BuildMayHaveEffect "a") requested
if lost.Phase<>Cleanup||lost.UnknownBuilds<>set["a"]||lost.Qualification<>QualificationUnknown then fail "lost build acknowledgement was not unknown"
let cancelled=Runner.cancel {acquired with Phase=CreatingStores;Owned=Map.ofList["a","owned-a"]}
if cancelled.Phase<>Cleanup||not cancelled.Cancelled then fail "cancellation did not enter cleanup"
printfn "persistent v3 image closure: trusted role manifests plus 17 predicate-specific refusals passed; typed runner executed %d ordered effects" trace.Length
Directory.Delete(root,true)
Directory.Delete(manifestRoot,true)
Directory.Delete(contractRoot,true)
Directory.Delete(contractManifestRoot,true)
Directory.Delete(runRoot,true)
Directory.Delete(lateRoot,true)
Directory.Delete(cancelledFinalRoot,true)
Directory.Delete(exceptionRoot,true)
Directory.Delete(blockedRoot,true)
Directory.Delete(cleanupRoot,true)
Directory.Delete(cancelRoot,true)
Directory.Delete(deadlineRoot,true)
