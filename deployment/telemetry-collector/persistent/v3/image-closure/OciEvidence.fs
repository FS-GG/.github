namespace FSGG.Telemetry.PersistentV3.ImageClosure

open System
open System.IO
open System.Formats.Tar
open System.IO.Compression
open System.Security.Cryptography
open System.Text.Json
open System.Collections.Generic
open System.Threading

type OciDescriptor={MediaType:string;Digest:string;Size:int64}
type OciFile={Path:string;Kind:string;Link:string;Bytes:int64;Sha256:string;Uid:int;Gid:int;Mode:int}
type OciResult={ArchiveSha256:string;ManifestDigest:string;ConfigDigest:string;Layers:OciDescriptor array;DiffIds:string array;Inventory:OciFile array;WrapperSha256:string;User:string}

module OciEvidence =
    let shaFile path=use input=File.OpenRead path in Convert.ToHexString(SHA256.HashData input).ToLowerInvariant()
    let shaBytes bytes=Convert.ToHexString(SHA256.HashData(bytes:byte array)).ToLowerInvariant()
    let private require condition reason=if not condition then raise(InvalidDataException reason)
    let private safePath (path:string)=not(String.IsNullOrWhiteSpace path)&&not(path.StartsWith('/'))&&not(path.Contains '\\')&&(path.Split('/')|>Array.forall(fun part->part<>".."&&part<>"."&&part<>""))
    let private strictJson path=
        require ((FileInfo path).Length<=16L*1024L*1024L) "oci-json-bound"
        let doc=JsonDocument.Parse(File.ReadAllBytes path)
        let rec closed (node:JsonElement)=
            match node.ValueKind with
            | JsonValueKind.Object->let names=HashSet<string>() in for p in node.EnumerateObject() do require(names.Add p.Name) "oci-duplicate-json";closed p.Value
            | JsonValueKind.Array->for item in node.EnumerateArray() do closed item
            | _->()
        try closed doc.RootElement;doc with _->doc.Dispose();reraise()
    let private descriptor (node:JsonElement)=
        let d={MediaType=node.GetProperty("mediaType").GetString();Digest=node.GetProperty("digest").GetString();Size=node.GetProperty("size").GetInt64()}
        require (d.Size>0L&&d.Size<=8L*1024L*1024L*1024L&&System.Text.RegularExpressions.Regex.IsMatch(d.Digest,"^sha256:[0-9a-f]{64}$")) "oci-descriptor"
        d
    let private copyBounded (token:CancellationToken) limit (input:Stream) (output:Stream)=
        let buffer=Array.zeroCreate<byte> 65536
        let mutable total=0L
        let mutable count=input.Read(buffer,0,buffer.Length)
        while count>0 do
            token.ThrowIfCancellationRequested()
            total<-total+int64 count
            require(total<=limit) "oci-decompression-bound"
            output.Write(buffer,0,count)
            count<-input.Read(buffer,0,buffer.Length)
        total
    let read (token:CancellationToken) archive unpackRoot =
        require(File.Exists archive&&(FileInfo archive).Length>0L&&(FileInfo archive).Length<=8L*1024L*1024L*1024L) "oci-archive-bound"
        require(not(Directory.Exists unpackRoot)&&not(File.Exists unpackRoot)) "oci-unpack-not-fresh"
        Directory.CreateDirectory(unpackRoot,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)|>ignore
        let seen=HashSet<string>()
        use input=File.OpenRead archive
        use reader=new TarReader(input)
        let mutable entry=reader.GetNextEntry()
        let mutable total=0L
        while not(isNull entry) do
            token.ThrowIfCancellationRequested()
            let name=entry.Name.TrimEnd('/')
            require(safePath name&&seen.Add name&&seen.Count<=4096) "oci-archive-path-or-duplicate"
            let allowed=name="index.json"||name="oci-layout"||name="blobs"||name="blobs/sha256"||System.Text.RegularExpressions.Regex.IsMatch(name,"^blobs/sha256/[0-9a-f]{64}$")
            require allowed "oci-archive-unexpected-entry"
            if entry.EntryType=TarEntryType.Directory then Directory.CreateDirectory(Path.Combine(unpackRoot,name))|>ignore
            else
                require(entry.EntryType=TarEntryType.RegularFile||entry.EntryType=TarEntryType.V7RegularFile) "oci-archive-link-or-special"
                require(entry.Length>=0L&&entry.Length<=8L*1024L*1024L*1024L) "oci-blob-bound"
                total<-total+entry.Length
                require(total<=8L*1024L*1024L*1024L) "oci-total-bound"
                let path=Path.Combine(unpackRoot,name)
                Directory.CreateDirectory(Path.GetDirectoryName path)|>ignore
                use output=new FileStream(path,FileMode.CreateNew)
                if not(isNull entry.DataStream) then require(copyBounded token entry.Length entry.DataStream output=entry.Length) "oci-truncated-entry"
            entry<-reader.GetNextEntry()
        use layout=strictJson(Path.Combine(unpackRoot,"oci-layout"))
        require(layout.RootElement.GetProperty("imageLayoutVersion").GetString()="1.0.0") "oci-layout"
        use index=strictJson(Path.Combine(unpackRoot,"index.json"))
        require(index.RootElement.GetProperty("schemaVersion").GetInt32()=2) "oci-index-schema"
        let manifests=index.RootElement.GetProperty("manifests")
        require(manifests.GetArrayLength()=1) "oci-platform-ambiguous"
        let selected=descriptor manifests[0]
        require(selected.MediaType="application/vnd.oci.image.manifest.v1+json") "oci-manifest-media-type"
        let noVariant (node:JsonElement)=
            let mutable variant=Unchecked.defaultof<JsonElement>
            not(node.TryGetProperty("variant",&variant))||variant.GetString()=""
        let mutable platform=Unchecked.defaultof<JsonElement>
        if manifests[0].TryGetProperty("platform",&platform) then require(platform.GetProperty("os").GetString()="linux"&&platform.GetProperty("architecture").GetString()="amd64"&&noVariant platform) "oci-platform"
        let referenced=HashSet<string>()
        let blob d=
            let path=Path.Combine(unpackRoot,"blobs/sha256",d.Digest.Substring 7)
            require(File.Exists path&&(FileInfo path).Length=d.Size&&"sha256:"+shaFile path=d.Digest) "oci-blob-digest-size"
            referenced.Add d.Digest|>ignore
            path
        use manifest=strictJson(blob selected)
        require(manifest.RootElement.GetProperty("schemaVersion").GetInt32()=2) "oci-manifest-schema"
        let configDescriptor=descriptor(manifest.RootElement.GetProperty("config"))
        require(configDescriptor.MediaType="application/vnd.oci.image.config.v1+json") "oci-config-media-type"
        use config=strictJson(blob configDescriptor)
        require(config.RootElement.GetProperty("os").GetString()="linux"&&config.RootElement.GetProperty("architecture").GetString()="amd64"&&noVariant config.RootElement) "oci-config-platform"
        let layers=manifest.RootElement.GetProperty("layers").EnumerateArray()|>Seq.map descriptor|>Seq.toArray
        require(layers.Length>0&&layers.Length<=128) "oci-layer-count"
        let rootfs=config.RootElement.GetProperty("rootfs")
        require(rootfs.GetProperty("type").GetString()="layers") "oci-rootfs-type"
        let diffIds=rootfs.GetProperty("diff_ids").EnumerateArray()|>Seq.map _.GetString()|>Seq.toArray
        require(diffIds.Length=layers.Length) "oci-diff-count"
        let mutable user=Unchecked.defaultof<JsonElement>
        let configuration=config.RootElement.GetProperty("config")
        let selectedUser=if configuration.TryGetProperty("User",&user) then user.GetString() else ""
        let mutable totalUncompressed=0L
        let inventory=Dictionary<string,OciFile>(StringComparer.Ordinal)
        let removeTree prefix=
            inventory.Keys|>Seq.filter(fun key->key=prefix||key.StartsWith(prefix+"/",StringComparison.Ordinal))|>Seq.toArray|>Array.iter(fun key->inventory.Remove key|>ignore)
        for layerIndex in 0..layers.Length-1 do
            token.ThrowIfCancellationRequested()
            let layer=layers[layerIndex]
            let source=blob layer
            let tar=Path.Combine(unpackRoot,$"layer-{layerIndex}.tar")
            use compressed=File.OpenRead source
            use output=new FileStream(tar,FileMode.CreateNew)
            let gzip=layer.MediaType="application/vnd.oci.image.layer.v1.tar+gzip"
            require(gzip||layer.MediaType="application/vnd.oci.image.layer.v1.tar") "oci-layer-media-type"
            if gzip then
                use decompressor=new GZipStream(compressed,CompressionMode.Decompress)
                copyBounded token (8L*1024L*1024L*1024L) decompressor output|>ignore
            else copyBounded token (8L*1024L*1024L*1024L) compressed output|>ignore
            output.Dispose()
            totalUncompressed<-totalUncompressed+(FileInfo tar).Length
            require(totalUncompressed<=8L*1024L*1024L*1024L) "oci-total-decompression-bound"
            require((FileInfo tar).Length%512L=0L) "oci-truncated-layer"
            require("sha256:"+shaFile tar=diffIds[layerIndex]) "oci-diff-id"
            use layerInput=File.OpenRead tar
            use layerReader=new TarReader(layerInput)
            let layerRows=ResizeArray<OciFile>()
            let whiteouts=ResizeArray<string*bool>()
            let layerNames=HashSet<string>()
            let mutable row=layerReader.GetNextEntry()
            while not(isNull row) do
                token.ThrowIfCancellationRequested()
                let mutable name=row.Name.TrimEnd('/')
                while name.StartsWith("./",StringComparison.Ordinal) do name<-name.Substring 2
                // Leading './' is conventional; rooted paths and escaping components still refuse.
                require(not(row.Name.StartsWith('/'))&&(safePath name||(name="."&&row.EntryType=TarEntryType.Directory))&&layerNames.Add name&&layerNames.Count<=200000) "oci-layer-path-or-duplicate"
                let leaf=Path.GetFileName name
                if name="."&&row.EntryType=TarEntryType.Directory then ()
                elif leaf.StartsWith(".wh.",StringComparison.Ordinal) then
                    require(row.EntryType=TarEntryType.RegularFile&&row.Length=0L) "oci-whiteout"
                    let parent=Path.GetDirectoryName(name).Replace('\\','/')
                    whiteouts.Add(((if leaf=".wh..wh..opq" then parent else (if parent="" then "" else parent+"/")+leaf.Substring 4),(leaf=".wh..wh..opq")))
                else
                    let kind=match row.EntryType with TarEntryType.RegularFile|TarEntryType.V7RegularFile->"file"|TarEntryType.Directory->"directory"|TarEntryType.SymbolicLink->"symlink"|TarEntryType.HardLink->"hardlink"|_->raise(InvalidDataException "oci-layer-special")
                    let hash=
                        if kind="file" then
                            if isNull row.DataStream then shaBytes [||] else Convert.ToHexString(SHA256.HashData row.DataStream).ToLowerInvariant()
                        else ""
                    if kind="hardlink" then require(safePath row.LinkName) "oci-hardlink-path"
                    layerRows.Add {Path="/"+name;Kind=kind;Link=row.LinkName;Bytes=row.Length;Sha256=hash;Uid=row.Uid;Gid=row.Gid;Mode=int row.Mode}
                row<-layerReader.GetNextEntry()
            for path,opaque in whiteouts do
                let path="/"+path
                if opaque then inventory.Keys|>Seq.filter(fun key->key.StartsWith(path.TrimEnd('/')+"/",StringComparison.Ordinal))|>Seq.toArray|>Array.iter(fun key->inventory.Remove key|>ignore)
                else removeTree path
            for file in layerRows do
                if file.Kind<>"directory" then removeTree file.Path
                inventory[file.Path]<-file
            require(inventory.Count<=200000) "oci-inventory-bound"
            File.Delete tar
        let declared=seen|>Seq.filter(fun name->name.StartsWith("blobs/sha256/",StringComparison.Ordinal))|>Seq.map(fun name->"sha256:"+Path.GetFileName name)|>Set.ofSeq
        require(declared=Set.ofSeq referenced) "oci-unreferenced-blob"
        {ArchiveSha256=shaFile archive;ManifestDigest=selected.Digest;ConfigDigest=configDescriptor.Digest;Layers=layers;DiffIds=diffIds;Inventory=inventory.Values|>Seq.sortBy _.Path|>Seq.toArray;WrapperSha256=shaFile(Path.Combine(unpackRoot,"index.json"));User=selectedUser}
    let equivalent a b=a.ManifestDigest=b.ManifestDigest&&a.ConfigDigest=b.ConfigDigest&&a.Layers=b.Layers&&a.DiffIds=b.DiffIds&&a.Inventory=b.Inventory
    let verifySelected (selection:Selection) (result:OciResult)=
        require(result.User="32768:32768") "oci-user-mismatch"
        for selected in selection.Files do
            let mutable parent=Path.GetDirectoryName(selected.TargetPath)
            while parent<>"/"&&not(String.IsNullOrEmpty parent) do
                match result.Inventory|>Array.tryFind(fun file->file.Path=parent) with
                | Some ancestor when ancestor.Kind<>"directory"||(ancestor.Mode&&&0o022)<>0->raise(InvalidDataException "oci-selected-ancestor-custody")
                | _->()
                parent<-Path.GetDirectoryName parent
            let actual=result.Inventory|>Array.tryFind(fun file->file.Path=selected.TargetPath)
            match actual with
            | Some file when file.Kind="file"&&file.Bytes=selected.Bytes&&file.Sha256=selected.Sha256&&file.Uid=32768&&file.Gid=32768&&file.Mode=Convert.ToInt32(selected.Mode,8)->()
            | _->raise(InvalidDataException "oci-selected-inventory-mismatch")
