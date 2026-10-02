namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text.Json
open System.Text.RegularExpressions

type FileRow = { SourcePath:string; TargetPath:string; Bytes:int64; Sha256:string; Mode:string; SourceClass:string }
type Selection = { IdentityClass:string; AcquisitionRoot:string; OwnerUid:int; Platform:string; HostSourceRevision:string; HostReleaseId:int64; HostRunId:int64; HostArchiveSha256:string; HostPackageSha256:string; HostPayloadSha256:string; HostManifestSha256:string; HostJournalSha256:string; ManagerSourceRevision:string; ManagerSourceTree:string; ManagerArtifactSha256:string; ManagerManifestSha256:string; ManagerPreparedSha256:string; ManagerArchiveSha256:string; ManagerRunId:int64; ManagerArtifactId:int64; RuntimeImageDigest:string; RuntimeTreeSha256:string; NativeElfSha256:string; NativeProfileSha256:string; ReaderProfileSha256:string; CanonicalVerifierSha256:string; InventorySha256:string; Files:FileRow array }
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
    let RuntimeTreeSha256="ead4ece7808c4f07f75eebfae705448509c6621534967ded5868454396a2aa21"
    [<Literal>]
    let NativeElfSha256="167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9"
    [<Literal>]
    let NativeProfileSha256="1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34"
    [<Literal>]
    let CanonicalVerifierSha256="8d6a33beae9a4de84fa7a703809e9b1a1656359a085f92091cf56de3b77fd3ba"
    let private sha (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let private sha64 (value:string)=not(String.IsNullOrWhiteSpace value)&&Regex.IsMatch(value,"^[0-9a-f]{64}$")&&value<>String('0',64)
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
    let private custody root source expectedUid =
        let rec directories current =
            let info=DirectoryInfo current
            if not info.Exists||not(noLink info)||ownerUid current<>expectedUid||((File.GetUnixFileMode current)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then false
            elif current=root then true
            elif isNull info.Parent then false
            else directories info.Parent.FullName
        let file=FileInfo source
        noLink file&&ownerUid source=expectedUid&&directories file.DirectoryName
    let private physicalRows selection =
        try
            let root=Path.GetFullPath selection.AcquisitionRoot
            let rootInfo=DirectoryInfo root
            if not rootInfo.Exists||not(noLink rootInfo)||ownerUid root<>selection.OwnerUid||((File.GetUnixFileMode root)&&&(UnixFileMode.GroupWrite|||UnixFileMode.OtherWrite))<>enum 0 then Error "acquisition-root-custody"
            elif (Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)|>Seq.map(fun path->Path.GetRelativePath(root,path))|>Set.ofSeq)<>(selection.Files|>Array.map _.SourcePath|>Set.ofArray) then Error "acquisition-root-inventory-incomplete"
            else
                selection.Files|>Array.map(fun row->
                    let source=Path.GetFullPath(Path.Combine(root,row.SourcePath))
                    let prefix=root.TrimEnd(Path.DirectorySeparatorChar)+string Path.DirectorySeparatorChar
                    if Path.IsPathFullyQualified row.SourcePath||not(source.StartsWith(prefix,StringComparison.Ordinal))||not(row.TargetPath.StartsWith("/opt/fsgg/image/",StringComparison.Ordinal))||row.TargetPath.Contains("..") then Error "inventory-path"
                    else
                        let info=FileInfo source
                        if not info.Exists||not(noLink info)||info.Attributes.HasFlag FileAttributes.ReparsePoint||not(custody root source selection.OwnerUid) then Error "inventory-regular-file-or-custody"
                        elif info.Length<>row.Bytes||modeText source<>row.Mode||sha(File.ReadAllBytes source)<>row.Sha256 then Error "inventory-physical-mismatch"
                        else Ok(source,row))
                |>Array.fold(fun state next->match state,next with|Error e,_->Error e|_,Error e->Error e|Ok rows,Ok row->Ok(row::rows))(Ok [])
                |>Result.map(List.rev>>List.toArray)
        with _ -> Error "inventory-physical-read"
    let prepare selection =
        if String.IsNullOrWhiteSpace selection.InventorySha256||selection.Files.Length=0 then Unavailable "native-image-closure-inventory-acquisition-required"
        elif selection.IdentityClass<>"production"&&selection.IdentityClass<>"synthetic-test" then Refused "identity-class"
        elif selection.IdentityClass="production"&&not(fixedProduction selection) then Refused "fixed-production-identity-mismatch"
        elif selection.Platform<>"linux/amd64"||not(sha64 selection.ReaderProfileSha256&&sha64 selection.InventorySha256) then Refused "selection-shape"
        else
            let sorted=selection.Files|>Array.sortBy _.TargetPath
            if sorted<>selection.Files||(sorted|>Array.distinctBy _.TargetPath).Length<>sorted.Length then Refused "inventory-order-or-duplicate"
            elif sorted.Length>8192||(sorted|>Array.sumBy _.Bytes)>1024L*1024L*1024L then Refused "inventory-bound"
            elif sorted|>Array.exists(fun row->String.IsNullOrWhiteSpace row.SourcePath||row.Bytes<=0L||not(sha64 row.Sha256)||(row.Mode<>"0444"&&row.Mode<>"0555")||not(Set.contains row.SourceClass (set["host";"manager";"native";"runtime"]))) then Refused "inventory-entry"
            elif (sorted|>Array.map _.SourceClass|>Set.ofArray)<>set["host";"manager";"native";"runtime"] then Refused "inventory-omits-dependency-root"
            elif sha(JsonSerializer.SerializeToUtf8Bytes sorted)<>selection.InventorySha256 then Refused "inventory-digest"
            else match physicalRows selection with
                 | Error reason->Refused reason
                 | Ok acquired->
                    let requiredProductionDigests=[|selection.HostPackageSha256;selection.ManagerArtifactSha256;selection.NativeElfSha256;selection.NativeProfileSha256;selection.ReaderProfileSha256;selection.CanonicalVerifierSha256|]
                    if selection.IdentityClass="production" && not(requiredProductionDigests|>Array.forall(fun digest->acquired|>Array.exists(fun(_,row)->row.Sha256=digest))) then Refused "inventory-omits-production-code-or-profile"
                    else
                      let copies=sorted|>Array.map(fun row -> $"COPY --chown=32768:32768 --chmod={row.Mode.Substring(1)} {row.SourcePath} {row.TargetPath}")
                      let containerfile=String.concat "\n" (Array.concat[ [|$"FROM docker.io/library/aspnet@{selection.RuntimeImageDigest}";"USER 32768:32768"|];copies;[|"ENTRYPOINT []";"CMD []"|] ])+"\n"
                      let profile=JsonSerializer.Serialize({|schema="fsgg.telemetry.persistent-v3-image-profile/1";user=32768;group=32768;platform=selection.Platform;argv=Array.empty<string>;listeners=Array.empty<string>;serviceEnabled=false;activationAuthorized=false;readerProfileSha256=selection.ReaderProfileSha256|})
                      Prepared(JsonSerializer.SerializeToUtf8Bytes({|schema="fsgg.telemetry.persistent-v3-image-context/2";status=(if selection.IdentityClass="production" then "prepared-inactive" else "prepared-inactive-synthetic");identityClass=selection.IdentityClass;containerfile=containerfile;profile=profile;inventorySha256=selection.InventorySha256;runtimeImageDigest=selection.RuntimeImageDigest;files=sorted|}))
