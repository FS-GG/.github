namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

type FileRow = { Path:string; Bytes:int64; Sha256:string; Mode:string; SourceClass:string }
type Selection = { Platform:string; HostSourceRevision:string; HostReleaseId:int64; HostRunId:int64; HostArchiveSha256:string; HostPackageSha256:string; HostPayloadSha256:string; HostManifestSha256:string; HostJournalSha256:string; ManagerSourceRevision:string; ManagerSourceTree:string; ManagerArtifactSha256:string; ManagerManifestSha256:string; ManagerPreparedSha256:string; ManagerArchiveSha256:string; ManagerRunId:int64; ManagerArtifactId:int64; RuntimeImageDigest:string; RuntimeTreeSha256:string; NativeElfSha256:string; NativeProfileSha256:string; ReaderProfileSha256:string; CanonicalVerifierSha256:string; InventorySha256:string; Files:FileRow array }
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
    let private sha64 (value:string)=Regex.IsMatch(value,"^[0-9a-f]{64}$") && value<>String('0',64)
    let private safePath (value:string)=value.StartsWith("/opt/fsgg/image/",StringComparison.Ordinal) && not(value.Contains("..")) && not(value.Contains('\\'))
    let prepare selection =
        if String.IsNullOrWhiteSpace selection.InventorySha256 || selection.Files.Length=0 then Unavailable "native-image-closure-inventory-acquisition-required"
        elif selection.Platform<>"linux/amd64" || selection.HostSourceRevision<>HostSourceRevision || selection.HostReleaseId<>401538149L || selection.HostRunId<>36964135216L || selection.HostArchiveSha256<>HostArchiveSha256 || selection.HostPackageSha256<>HostPackageSha256 || selection.HostPayloadSha256<>HostPayloadSha256 || selection.HostManifestSha256<>HostManifestSha256 || selection.HostJournalSha256<>HostJournalSha256 || selection.ManagerSourceRevision<>ManagerSourceRevision || selection.ManagerSourceTree<>ManagerSourceTree || selection.ManagerArtifactSha256<>ManagerArtifactSha256 || selection.ManagerManifestSha256<>ManagerManifestSha256 || selection.ManagerPreparedSha256<>ManagerPreparedSha256 || selection.ManagerArchiveSha256<>ManagerArchiveSha256 || selection.ManagerRunId<>36983338783L || selection.ManagerArtifactId<>11216418410L || selection.RuntimeImageDigest<>RuntimeImageDigest || selection.RuntimeTreeSha256<>RuntimeTreeSha256 || selection.NativeElfSha256<>NativeElfSha256 || selection.NativeProfileSha256<>NativeProfileSha256 || selection.CanonicalVerifierSha256<>CanonicalVerifierSha256 then Refused "fixed-production-identity-mismatch"
        elif not(sha64 selection.ReaderProfileSha256 && sha64 selection.InventorySha256) then Refused "invalid-digest"
        else
            let sorted=selection.Files|>Array.sortBy _.Path
            if sorted<>selection.Files || (sorted|>Array.distinctBy _.Path).Length<>sorted.Length then Refused "inventory-order-or-duplicate"
            elif sorted.Length>8192 || (sorted|>Array.sumBy _.Bytes)>1024L*1024L*1024L then Refused "inventory-bound"
            elif sorted|>Array.exists(fun row->not(safePath row.Path)||row.Bytes<=0L||not(sha64 row.Sha256)||row.Mode<>"0444"&&row.Mode<>"0555"||row.SourceClass<>"host"&&row.SourceClass<>"manager"&&row.SourceClass<>"native"&&row.SourceClass<>"runtime") then Refused "inventory-entry"
            elif (sorted|>Array.map _.SourceClass|>Set.ofArray)<>set ["host";"manager";"native";"runtime"] || not(sorted|>Array.exists(fun row->row.SourceClass="native"&&row.Sha256=NativeElfSha256)) || not(sorted|>Array.exists(fun row->row.Sha256=CanonicalVerifierSha256)) then Refused "inventory-omits-required-content"
            else
                let inventoryBytes=JsonSerializer.SerializeToUtf8Bytes(sorted)
                if sha inventoryBytes<>selection.InventorySha256 then Refused "inventory-digest"
                else Prepared(JsonSerializer.SerializeToUtf8Bytes({|schema="fsgg.telemetry.persistent-v3-image-context/1"; status="prepared-inactive"; platform=selection.Platform; user=32768; activationAuthorized=false; serviceEnabled=false; host={|sourceRevision=selection.HostSourceRevision;releaseId=selection.HostReleaseId;runId=selection.HostRunId;archiveSha256=selection.HostArchiveSha256;packageSha256=selection.HostPackageSha256;payloadSha256=selection.HostPayloadSha256;manifestSha256=selection.HostManifestSha256;journalSha256=selection.HostJournalSha256|}; manager={|sourceRevision=selection.ManagerSourceRevision;sourceTree=selection.ManagerSourceTree;runId=selection.ManagerRunId;artifactId=selection.ManagerArtifactId;artifactSha256=selection.ManagerArtifactSha256;manifestSha256=selection.ManagerManifestSha256;preparedSha256=selection.ManagerPreparedSha256;archiveSha256=selection.ManagerArchiveSha256|}; runtimeImageDigest=selection.RuntimeImageDigest; runtimeTreeSha256=selection.RuntimeTreeSha256; nativeElfSha256=selection.NativeElfSha256; nativeProfileSha256=selection.NativeProfileSha256; readerProfileSha256=selection.ReaderProfileSha256; canonicalVerifierSha256=selection.CanonicalVerifierSha256; inventorySha256=selection.InventorySha256; files=sorted|}))
