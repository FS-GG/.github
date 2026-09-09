namespace FS.GG.Coord.Cli

#nowarn "3391"

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Threading
open System.Runtime.InteropServices
open FS.GG.Coord
open FS.GG.Telemetry

module WorkspaceTelemetryApplication =
    module private Native =
        [<Literal>]
        let LockExclusive = 2
        [<Literal>]
        let LockNonBlocking = 4
        [<DllImport("libc", SetLastError = true)>]
        extern int flock(int descriptor, int operation)
        [<DllImport("libc", SetLastError = true)>]
        extern int fsync(int descriptor)
        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openDirectory(string path, int flags)
        [<DllImport("libc", EntryPoint = "open", SetLastError = true)>]
        extern int openPrivateFile(string path, int flags, uint32 mode)
        [<DllImport("libc", SetLastError = true)>]
        extern int close(int descriptor)
    [<Literal>]
    let Schema = "fsgg.telemetry.workspace-config/1"
    let private green = ExitCode.toInt ExitCode.Green
    let private red = ExitCode.toInt ExitCode.Error
    type Destination = Local of string | Remote of Uri * string * string
    type Association = { Workspace:string; Producer:string; Stream:string; Repositories:string list; Destination:Destination }
    type Config = { Path:string; Engine:string; Associations:Association list; Retired:Association list }
    type Binding = { ConfigPath:string; Repository:string; Producer:string; Digest:string }

    let private option name args = args |> List.indexed |> List.rev |> List.tryPick(fun (i,v)->if v=name then List.tryItem(i+1) args else None)
    let private options name args = args |> List.indexed |> List.choose(fun (i,v)->if v=name then List.tryItem(i+1) args else None)
    let private defaultConfig () =
        match Environment.GetEnvironmentVariable "FSGG_TELEMETRY_CONFIG" with
        | null | "" ->
            match Environment.GetEnvironmentVariable "XDG_CONFIG_HOME" with
            | null | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile,".config","fs-gg","telemetry.json")
            | value -> Path.Combine(value,"fs-gg","telemetry.json")
        | value -> value
    let private configPath explicitPath = explicitPath |> Option.defaultWith defaultConfig |> Path.GetFullPath
    let private validRepo (value:string) =
        let pieces=value.Split('/')
        let validComponent (piece:string) =
            not (String.IsNullOrWhiteSpace piece)
            && piece.Length <= 100
            && piece <> "."
            && piece <> ".."
            && piece |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '_' || c = '.')
        pieces.Length=2 && pieces |> Array.forall validComponent
    let private checkPrivateFile path =
        let info=FileInfo path
        if not info.Exists then Error ["unconfigured"]
        elif not (isNull info.LinkTarget) || info.Length>65536L then Error ["configuration-unsafe"]
        elif not (OperatingSystem.IsWindows()) && File.GetUnixFileMode(path)<>(UnixFileMode.UserRead|||UnixFileMode.UserWrite) then Error ["configuration-unsafe"]
        else Ok()
    let private exactObject (node:JsonObject) names =
        node |> Seq.map _.Key |> Set.ofSeq = Set.ofList names
    let private noDuplicateProperties (element:JsonElement) =
        let rec valid (value:JsonElement) =
            match value.ValueKind with
            | JsonValueKind.Object ->
                let names = value.EnumerateObject() |> Seq.map _.Name |> Seq.toList
                names.Length = (names |> List.distinct |> List.length)
                && (value.EnumerateObject() |> Seq.forall(fun property -> valid property.Value))
            | JsonValueKind.Array -> value.EnumerateArray() |> Seq.forall valid
            | _ -> true
        valid element
    let private text (node:JsonObject) (name:string) = match node[name] with null -> None | value -> value.GetValue<string>() |> Some
    let private parseAssociation (node:JsonObject) =
        try
            if not(exactObject node ["workspaceId";"producerId";"streamId";"repositories";"destination"]) then Error "configuration-shape" else
            let workspace=text node "workspaceId" |> Option.defaultValue ""
            let producer=text node "producerId" |> Option.defaultValue ""
            let stream=text node "streamId" |> Option.defaultValue ""
            let repositories=node["repositories"].AsArray() |> Seq.map _.GetValue<string>() |> Seq.toList
            let destination=node["destination"].AsObject()
            if [workspace;producer;stream] |> List.exists(TelemetryReceipt.validId >> not) || repositories.IsEmpty || repositories |> List.exists(validRepo >> not) || repositories.Length<>(repositories|>List.distinct|>List.length) then Error "configuration-identity" else
            match text destination "kind" with
            | Some "local" when exactObject destination ["kind";"storeRoot"] ->
                let root=text destination "storeRoot" |> Option.defaultValue ""
                if not(Path.IsPathFullyQualified root) then Error "configuration-path" else Ok {Workspace=workspace;Producer=producer;Stream=stream;Repositories=repositories;Destination=Local(Path.GetFullPath root)}
            | Some "remote" when exactObject destination ["kind";"endpoint";"credentialReference";"spoolRoot"] ->
                let endpoint=text destination "endpoint" |> Option.defaultValue "" |> Uri
                let credential=text destination "credentialReference" |> Option.defaultValue ""
                let spool=text destination "spoolRoot" |> Option.defaultValue ""
                let candidate:RemoteContract.ClientConfig={Endpoint=endpoint;CredentialReference=credential}
                match RemoteContract.validateClientConfig candidate with
                | Error _ -> Error "configuration-remote"
                | Ok _ when not(Path.IsPathFullyQualified spool) -> Error "configuration-remote"
                | Ok _ -> Ok {Workspace=workspace;Producer=producer;Stream=stream;Repositories=repositories;Destination=Remote(endpoint,credential,Path.GetFullPath spool)}
            | _ -> Error "configuration-destination"
        with _ -> Error "configuration-shape"
    let private load explicitPath =
        let path=configPath explicitPath
        match checkPrivateFile path with
        | Error errors -> Error errors
        | Ok () ->
            try
                let bytes = File.ReadAllBytes path
                use document = JsonDocument.Parse bytes
                if not (noDuplicateProperties document.RootElement) then Error ["configuration-duplicate-property"] else
                let root=JsonNode.Parse(bytes).AsObject()
                if not(exactObject root ["schema";"engine";"associations";"retiredAssociations"]) || text root "schema"<>Some Schema then Error ["configuration-version"] else
                let engine=text root "engine" |> Option.defaultValue ""
                if String.IsNullOrWhiteSpace engine || engine.Contains('/') || engine.Contains('\\') then Error ["configuration-engine"] else
                let parsed=root["associations"].AsArray() |> Seq.map(fun x->parseAssociation(x.AsObject())) |> Seq.toList
                match parsed |> List.tryPick(function Error e->Some e|_->None) with
                | Some error -> Error [error]
                | None ->
                    let associations=parsed |> List.choose(function Ok x->Some x|_->None)
                    let retiredParsed=root["retiredAssociations"].AsArray() |> Seq.map(fun x->parseAssociation(x.AsObject())) |> Seq.toList
                    match retiredParsed |> List.tryPick(function Error e->Some e|_->None) with
                    | Some error -> Error [error]
                    | None ->
                        let retired=retiredParsed |> List.choose(function Ok x->Some x|_->None)
                        let repos=associations |> List.collect _.Repositories |> List.map _.ToLowerInvariant()
                        let scopes=associations |> List.map(fun a->a.Workspace,a.Producer,a.Stream)
                        let workspaces=associations |> List.map _.Workspace
                        let producers=(associations@retired) |> List.map _.Producer
                        if repos.Length<>(repos|>List.distinct|>List.length) || scopes.Length<>(scopes|>List.distinct|>List.length)
                           || workspaces.Length<>(workspaces|>List.distinct|>List.length) || producers.Length<>(producers|>List.distinct|>List.length) then Error ["configuration-ambiguous"]
                        else Ok {Path=path;Engine=engine;Associations=associations;Retired=retired}
            with _ -> Error ["configuration-unreadable"]
    let private repository supplied =
        supplied
        |> Option.orElseWith(fun()->Environment.GetEnvironmentVariable("FSGG_TELEMETRY_REPOSITORY") |> Option.ofObj)
        |> Option.orElseWith(fun()->Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") |> Option.ofObj)
        |> Option.filter validRepo
    let private select supplied config =
        match repository supplied with
        | None -> Error ["repository-required"]
        | Some repo -> match config.Associations |> List.filter(fun a->a.Repositories |> List.exists(fun value->String.Equals(value,repo,StringComparison.OrdinalIgnoreCase))) with [one]->Ok one | []->Error ["workspace-unassociated"] | _->Error ["workspace-ambiguous"]
    let private scope a : TelemetryReceipt.Scope={Workspace=a.Workspace;Producer=a.Producer;Stream=a.Stream}
    let private associationDigest association =
        let destination = match association.Destination with Local root -> "local\n"+root | Remote(endpoint,reference,spool) -> "remote\n"+endpoint.AbsoluteUri+"\n"+reference+"\n"+spool
        Encoding.UTF8.GetBytes(String.concat "\n" ([association.Workspace;association.Producer;association.Stream;destination]@association.Repositories))
        |> SHA256.HashData |> Convert.ToHexString |> _.ToLowerInvariant()
    let private envelope (a:Association) (payload:byte array) =
        try
            let payloadNode = JsonNode.Parse(payload).AsObject()
            let originalIngestId = payloadNode["ingestId"].GetValue<string>()
            let batchId=if TelemetryReceipt.validId originalIngestId then originalIngestId else "batch-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes originalIngestId)).ToLowerInvariant()
            payloadNode["ingestId"] <- batchId
            let normalizedPayload = Encoding.UTF8.GetBytes(payloadNode.ToJsonString(JsonSerializerOptions(WriteIndented=false)))
            TelemetryStore.parseBatch normalizedPayload |> Result.defaultWith(fun _->invalidArg "payload" "invalid ingest") |> ignore
            let root=JsonObject()
            root["schema"]<-"fsgg.telemetry.envelope/1";root["workspaceId"]<-a.Workspace;root["producerId"]<-a.Producer;root["streamId"]<-a.Stream;root["batchId"]<-batchId
            root["payload"]<-payloadNode
            let bytes=Encoding.UTF8.GetBytes(root.ToJsonString(JsonSerializerOptions(WriteIndented=false)))
            TelemetryReceipt.parse bytes |> Result.map(fun value->Encoding.UTF8.GetBytes value.Canonical,value)
        with _ -> Error ["invalid-request"]
    let private ensurePrivateDirectory (path:string) =
        try
            if not(Path.IsPathFullyQualified path) then Error ["spool-path-invalid"] else
            let parent = DirectoryInfo(Path.GetDirectoryName path)
            if not parent.Exists || not(isNull parent.LinkTarget) || (not(OperatingSystem.IsWindows()) && (File.GetUnixFileMode(parent.FullName) &&& (UnixFileMode.GroupRead|||UnixFileMode.GroupWrite|||UnixFileMode.GroupExecute|||UnixFileMode.OtherRead|||UnixFileMode.OtherWrite|||UnixFileMode.OtherExecute))<>enum 0) then Error ["spool-parent-unsafe"] else
            if not(Directory.Exists path) then
                Directory.CreateDirectory path |> ignore
                if not(OperatingSystem.IsWindows()) then File.SetUnixFileMode(path,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
            let info=DirectoryInfo path
            if not(isNull info.LinkTarget) || (not(OperatingSystem.IsWindows()) && File.GetUnixFileMode(path)<>(UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)) then Error ["spool-path-unsafe"] else Ok path
        with _ -> Error ["spool-unavailable"]
    let private safeAncestors path =
        let rec loop (directory:DirectoryInfo) =
            if isNull directory then true
            elif directory.Exists && not(isNull directory.LinkTarget) then false
            else loop directory.Parent
        loop (DirectoryInfo path)
    let private flushDirectory path =
        if OperatingSystem.IsLinux() then
            let descriptor = Native.openDirectory(path, 0x10000 ||| 0x80000)
            if descriptor < 0 then raise (IOException $"cannot open directory for durability sync: %s{path}")
            try
                if Native.fsync descriptor <> 0 then raise (IOException $"cannot sync directory: %s{path}")
            finally
                Native.close descriptor |> ignore
    let private atomicWrite directory name bytes =
        let target = Path.Combine(directory, name)
        let temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp")
        use output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)
        if not (OperatingSystem.IsWindows()) then
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        output.Write bytes
        output.Flush true
        output.Close()
        File.Move(temporary, target, false)
        flushDirectory directory
        target
    let private credential (reference:string) (_:CancellationToken) =
        task {
            let name = "FSGG_TELEMETRY_CREDENTIAL_" + reference.Replace('-', '_').ToUpperInvariant()
            return Environment.GetEnvironmentVariable name |> Option.ofObj |> Option.filter (String.IsNullOrWhiteSpace >> not)
        }
    let private remoteOne (a:Association) (endpoint:Uri) (reference:string) (token:CancellationToken) (file:string) =
        let info=FileInfo file
        if not info.Exists || not(isNull info.LinkTarget) || info.Length>int64 TelemetryReceipt.MaxEnvelopeBytes || (not(OperatingSystem.IsWindows()) && File.GetUnixFileMode(file)<>(UnixFileMode.UserRead|||UnixFileMode.UserWrite)) then invalidOp "unsafe spool entry"
        let bytes=File.ReadAllBytes file
        match TelemetryReceipt.parse bytes with
        | Error _ -> Error ["spool-invalid"]
        | Ok parsed when parsed.Scope<>(scope a) -> Error ["spool-scope-conflict"]
        | Ok parsed ->
            let config:RemoteContract.ClientConfig={Endpoint=endpoint;CredentialReference=reference}
            let resolve=credential
            let result = (RemoteClient.lookup config resolve (scope a) parsed.BatchId parsed.Digest token).GetAwaiter().GetResult()
            let result =
                match result with
                | RemoteClient.Unacknowledged "receipt-unavailable" -> (RemoteClient.submit config resolve (scope a) bytes token).GetAwaiter().GetResult()
                | other -> other
            match result with
            | RemoteClient.Acknowledged receipt when receipt.Status="durably-received" || receipt.Status="applied" || receipt.Status="rejected" || receipt.Status="expired" ->
                let outcomeDirectory=Path.Combine(Path.GetDirectoryName(file),"outcomes")
                match ensurePrivateDirectory outcomeDirectory with
                | Error errors -> Error errors
                | Ok directory ->
                    let outcome=Encoding.UTF8.GetBytes(JsonSerializer.Serialize {|schema="fsgg.telemetry.workspace-outcome/1";batchId=receipt.BatchId;digest=receipt.Digest;status=receipt.Status;code=receipt.Code|}+"\n")
                    let outcomeName=Path.GetFileNameWithoutExtension(file)+".json"
                    if not(File.Exists(Path.Combine(directory,outcomeName))) then
                        let retained = Directory.EnumerateFiles(directory,"*.json") |> Seq.truncate 129 |> Seq.map FileInfo |> Seq.toArray
                        if retained.Length >= 128 then
                            let oldest = retained |> Array.minBy _.LastWriteTimeUtc
                            File.Delete oldest.FullName
                            flushDirectory directory
                        atomicWrite directory outcomeName outcome |> ignore
                    File.Delete file; flushDirectory(Path.GetDirectoryName file); Ok receipt.Status
            | RemoteClient.Unacknowledged code when List.contains code ["identity-conflict";"unauthorized-scope";"unsupported-version";"invalid-request";"oversized-batch"] -> Error ["terminal:"+code]
            | _ -> Error ["unacknowledged-lossy"]
    let private withLock config action =
        let lockPath=config+".lock"
        try
            if OperatingSystem.IsLinux() then
                let descriptor = Native.openPrivateFile(lockPath, 0x2 ||| 0x40 ||| 0x20000 ||| 0x80000, 0x180u)
                if descriptor < 0 then Error ["configuration-unsafe"]
                else
                    try if Native.flock(descriptor, Native.LockExclusive ||| Native.LockNonBlocking) <> 0 then Error ["configuration-busy"] else action()
                    finally Native.close descriptor |> ignore
            else
                use held=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)
                action()
        with :? IOException -> Error ["configuration-busy"]
    let isConfigured configArg =
        match load configArg with Ok _ -> true | _ -> false
    let configuredPath configArg = configPath configArg
    let tryBinding configArg repositoryArg =
        match load configArg with
        | Ok config ->
            match select repositoryArg config, repository repositoryArg with
            | Ok association, Some repo -> Some(config.Path, repo, associationDigest association)
            | _ -> None
        | Error _ -> None
    let selectedProducer configArg repositoryArg =
        load configArg |> Result.bind(select repositoryArg) |> Result.toOption |> Option.map _.Producer
    let resolveBinding configArg repositoryArg =
        match load configArg with
        | Error errors -> Error errors
        | Ok config ->
            match select repositoryArg config, repository repositoryArg with
            | Ok association, Some repo -> Ok {ConfigPath=config.Path;Repository=repo;Producer=association.Producer;Digest=associationDigest association}
            | Error errors, _ -> Error errors
            | _ -> Error ["repository-required"]
    let tryPublishBound configArg repositoryArg (expectedProducer:string option) (expectedBinding:string option) payload =
        let path=configPath configArg
        if not(File.Exists path) then Error ["unconfigured"] else
        try withLock path (fun () ->
            match load (Some path) with
            | Error errors -> Error errors
            | Ok config ->
                match select repositoryArg config with
                | Error errors -> Error errors
                | Ok association when expectedProducer.IsSome && expectedProducer <> Some association.Producer -> Error ["association-stale"]
                | Ok association when expectedBinding.IsSome && expectedBinding <> Some(associationDigest association) -> Error ["association-stale"]
                | Ok association ->
                    match envelope association payload with
                    | Error errors -> Error errors
                    | Ok(bytes, parsed) ->
                        match association.Destination with
                        | Local root ->
                            let assessment = TelemetryStoreApplication.assessProductionRoot root
                            TelemetryStoreApplication.submitReceipt root assessment (scope association) bytes
                        | Remote(endpoint, reference, spool) ->
                            match ensurePrivateDirectory spool with
                            | Error errors -> Error errors
                            | Ok directory ->
                                let name = TelemetryReceipt.key association.Producer parsed.BatchId + ".ready"
                                let file = Path.Combine(directory, name)
                                let outcomeFile = Path.Combine(directory,"outcomes",TelemetryReceipt.key association.Producer parsed.BatchId+".json")
                                let outcomeConflict =
                                    if not (File.Exists outcomeFile) then false
                                    else
                                        let info = FileInfo outcomeFile
                                        if info.Length > 4096L || not (isNull info.LinkTarget) then true
                                        else
                                            use outcome=JsonDocument.Parse(File.ReadAllBytes outcomeFile)
                                            outcome.RootElement.GetProperty("digest").GetString()<>parsed.Digest
                                if outcomeConflict then Error ["identity-conflict"]
                                elif File.Exists file then
                                    let info = FileInfo file
                                    if info.Length > int64 TelemetryReceipt.MaxEnvelopeBytes || not (isNull info.LinkTarget)
                                       || (not(OperatingSystem.IsWindows()) && File.GetUnixFileMode(file)<>(UnixFileMode.UserRead|||UnixFileMode.UserWrite)) then Error ["identity-conflict"]
                                    else
                                        match TelemetryReceipt.parse(File.ReadAllBytes file) with
                                        | Ok prior when prior.Digest=parsed.Digest -> remoteOne association endpoint reference CancellationToken.None file
                                        | _ -> Error ["identity-conflict"]
                                else
                                    let files=Directory.EnumerateFiles(directory,"*.ready") |> Seq.truncate 129 |> Seq.toArray
                                    let total=files |> Array.sumBy(fun file->FileInfo(file).Length)
                                    if files.Length>=128 || total+int64 bytes.Length>8L*1024L*1024L then Error ["spool-overload"]
                                    else
                                        atomicWrite directory name bytes |> ignore
                                        remoteOne association endpoint reference CancellationToken.None file)
        with _ -> Error ["publication-advisory-failure"]
    let tryPublishExpected configArg repositoryArg expectedProducer payload = tryPublishBound configArg repositoryArg expectedProducer None payload
    let tryPublish configArg repositoryArg payload = tryPublishExpected configArg repositoryArg None payload
    let private tryDrainBound configArg repositoryArg (expectedBinding:string option) =
        let path=configPath configArg
        if not(File.Exists path) then Error ["unconfigured"] else
        try withLock path (fun () ->
            match load (Some path) with
            | Error errors -> Error errors
            | Ok config ->
                match select repositoryArg config with
                | Error errors -> Error errors
                | Ok association when expectedBinding.IsSome && expectedBinding <> Some(associationDigest association) -> Error ["association-stale"]
                | Ok association ->
                    match association.Destination with
                    | Local root -> TelemetryStoreApplication.drainReceipts root (TelemetryStoreApplication.assessProductionRoot root) association.Workspace
                    | Remote(endpoint, reference, spool) ->
                        match ensurePrivateDirectory spool with
                        | Error errors -> Error errors
                        | Ok directory ->
                            use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 30.)
                            let files = Directory.EnumerateFiles(directory, "*.ready") |> Seq.truncate 16 |> Seq.toList
                            let results = files |> List.map (remoteOne association endpoint reference deadline.Token)
                            match results |> List.tryPick (function Error e -> Some e | _ -> None) with
                            | Some errors -> Error errors
                            | None -> Ok $"{{\"schema\":\"fsgg.telemetry.workspace-drain/1\",\"processed\":{files.Length}}}\n")
        with _ -> Error ["drain-advisory-failure"]
    let tryDrain configArg repositoryArg = tryDrainBound configArg repositoryArg None
    let tryPublishBinding binding payload = tryPublishBound (Some binding.ConfigPath) (Some binding.Repository) (Some binding.Producer) (Some binding.Digest) payload
    let tryDrainBinding binding = tryDrainBound (Some binding.ConfigPath) (Some binding.Repository) (Some binding.Digest)
    let privateStateRoot configArg repositoryArg =
        load configArg
        |> Result.bind (select repositoryArg)
        |> Result.map (fun association ->
            match association.Destination with
            | Local root -> root
            | Remote(_, _, spool) -> spool)
    let tryLocalStoreRoot configArg repositoryArg =
        load configArg
        |> Result.bind (select repositoryArg)
        |> Result.map (fun association -> match association.Destination with Local root -> Some root | Remote _ -> None)
    let tryLocalStoreRootBound binding =
        load (Some binding.ConfigPath)
        |> Result.bind(select (Some binding.Repository))
        |> Result.bind(fun association ->
            if associationDigest association <> binding.Digest then Error ["association-stale"]
            else Ok(match association.Destination with Local root -> Some root | Remote _ -> None))
    let private serialize (path:string) (associations:Association list) (retired:Association list) =
        let root = JsonObject()
        root["schema"] <- Schema
        root["engine"] <- "fsgg-coord-engine"
        let values = JsonArray()
        for a in associations do
            let node = JsonObject()
            node["workspaceId"] <- a.Workspace
            node["producerId"] <- a.Producer
            node["streamId"] <- a.Stream
            let repos = JsonArray()
            a.Repositories |> List.iter repos.Add
            node["repositories"] <- repos
            let destination = JsonObject()
            match a.Destination with
            | Local store -> destination["kind"] <- "local"; destination["storeRoot"] <- store
            | Remote(endpoint, reference, spool) -> destination["kind"] <- "remote"; destination["endpoint"] <- endpoint.AbsoluteUri; destination["credentialReference"] <- reference; destination["spoolRoot"] <- spool
            node["destination"] <- destination
            values.Add node
        root["associations"] <- values
        let retiredValues = JsonArray()
        for a in retired do
            let node = JsonObject()
            node["workspaceId"] <- a.Workspace
            node["producerId"] <- a.Producer
            node["streamId"] <- a.Stream
            let repos = JsonArray()
            a.Repositories |> List.iter repos.Add
            node["repositories"] <- repos
            let destination = JsonObject()
            match a.Destination with
            | Local store -> destination["kind"] <- "local"; destination["storeRoot"] <- store
            | Remote(endpoint,reference,spool) -> destination["kind"] <- "remote"; destination["endpoint"] <- endpoint.AbsoluteUri; destination["credentialReference"] <- reference; destination["spoolRoot"] <- spool
            node["destination"] <- destination
            retiredValues.Add node
        root["retiredAssociations"] <- retiredValues
        let bytes = Encoding.UTF8.GetBytes(root.ToJsonString(JsonSerializerOptions(WriteIndented=false)) + "\n")
        if bytes.Length > 65536 then invalidOp "configuration-capacity"
        let directory = Path.GetDirectoryName path
        let directoryInfo = DirectoryInfo directory
        if not directoryInfo.Exists || not (safeAncestors directory) || not (isNull directoryInfo.LinkTarget)
           || (not(OperatingSystem.IsWindows()) && File.GetUnixFileMode(directory)<>(UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)) then
            invalidOp "configuration-directory-unsafe"
        let temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
        use output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)
        output.Write bytes; output.Flush true; output.Close()
        if not (OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary, UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
        File.Move(temporary, path, true)
        flushDirectory directory
    let private activate kind args =
        try
            if not(OperatingSystem.IsLinux()) || Runtime.InteropServices.RuntimeInformation.ProcessArchitecture<>Runtime.InteropServices.Architecture.X64 then Error ["unsupported-platform"] else
            let required name =
                match option name args |> Option.filter(String.IsNullOrWhiteSpace >> not) with
                | Some value -> Ok value
                | None -> Error ($"{name} is required")
            match required "--workspace",required "--producer",required "--stream",options "--repository" args with
            | Ok workspace,Ok producer,Ok stream,repositories when [workspace;producer;stream]|>List.forall TelemetryReceipt.validId && not repositories.IsEmpty && repositories|>List.forall validRepo ->
                let destination=
                    match kind,option "--store-root" args,option "--endpoint" args,option "--credential-reference" args,option "--spool-root" args with
                    | "local",Some root,_,_,_ when Path.IsPathFullyQualified root->Some(Local(Path.GetFullPath root))
                    | "remote",_,Some endpoint,Some reference,Some spool when Path.IsPathFullyQualified spool && TelemetryReceipt.validId reference ->
                        let uri = Uri endpoint
                        let config:RemoteContract.ClientConfig={Endpoint=uri;CredentialReference=reference}
                        match RemoteContract.validateClientConfig config with Ok _ -> Some(Remote(uri,reference,Path.GetFullPath spool)) | Error _ -> None
                    | _->None
                match destination with
                | None->Error ["invalid destination"]
                | Some(Local root as selected)->
                    let assessment=TelemetryStoreApplication.assessProductionRoot root
                    TelemetryStoreApplication.initialize root assessment |> Result.bind(fun _->TelemetryStoreApplication.enrollReceiptProducer root assessment {Workspace=workspace;Producer=producer;Stream=stream}) |> Result.map(fun _->{Workspace=workspace;Producer=producer;Stream=stream;Repositories=repositories;Destination=selected})
                | Some(Remote(_,reference,spool) as selected) ->
                    let available=(credential reference CancellationToken.None).GetAwaiter().GetResult().IsSome
                    if not available then Error ["credential-unavailable"] else ensurePrivateDirectory spool |> Result.map(fun _->{Workspace=workspace;Producer=producer;Stream=stream;Repositories=repositories;Destination=selected})
                |> Result.bind (fun association ->
                    let path=configPath(option "--config" args)
                    let directory=Path.GetDirectoryName path
                    if not(Directory.Exists directory) then
                        let parent=DirectoryInfo(Path.GetDirectoryName directory)
                        if not parent.Exists || not(safeAncestors parent.FullName) || not(isNull parent.LinkTarget) then invalidOp "configuration-parent-unsafe"
                        Directory.CreateDirectory directory |> ignore
                        if not(OperatingSystem.IsWindows()) then File.SetUnixFileMode(directory,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
                    withLock path (fun()->
                        let existing,retired=match load(Some path) with Ok c->c.Associations,c.Retired | Error ["unconfigured"]->[],[] | Error errors->raise(InvalidOperationException(String.concat ";" errors))
                        if existing |> List.exists(fun a -> a.Workspace=association.Workspace || not (Set.isEmpty (Set.intersect (Set.ofList a.Repositories) (Set.ofList association.Repositories)))) then Error ["association-conflict"]
                        else serialize path (existing@[association]) retired;Ok "activated"))
            | _->Error ["invalid activation"]
        with error->Error [error.Message]
    let private status args =
        let path=option "--config" args
        match load path with
        | Error ["unconfigured"]->Ok "{\"schema\":\"fsgg.telemetry.workspace-status/1\",\"status\":\"unconfigured\"}\n"
        | Error errors->Error errors
        | Ok config ->
            match select (option "--repository" args) config with
            | Error errors -> Error errors
            | Ok a ->
                let kind, pending, lossy, census =
                    match a.Destination with
                    | Local root ->
                        match TelemetryStoreApplication.receiptCapacity root (TelemetryStoreApplication.assessProductionRoot root) with
                        | Ok(_,count,_) -> "local", int count, false, "indexed-only-recovery-not-performed"
                        | Error _ -> "local", -1, false, "unavailable"
                    | Remote(_,_,spool) -> "remote", (if Directory.Exists spool then Directory.EnumerateFiles(spool,"*.ready") |> Seq.truncate 129 |> Seq.length else 0), true, "bounded-ready-files"
                Ok(JsonSerializer.Serialize {|schema="fsgg.telemetry.workspace-status/1";status="configured";workspaceId=a.Workspace;producerId=a.Producer;streamId=a.Stream;destination=kind;pending=pending;pendingCensus=census;unacknowledgedLossy=lossy && pending>0|}+"\n")
    let private associate args =
        let path=configPath(option "--config" args)
        let workspace=option "--workspace" args
        let additions=options "--repository" args
        let removals=options "--remove-repository" args
        if not(File.Exists path) then Error ["unconfigured"]
        elif workspace.IsNone || (additions@removals |> List.exists(validRepo>>not)) then Error ["invalid association"] else
        withLock path (fun()->
            match load(Some path) with
            | Error errors->Error errors
            | Ok config ->
                match config.Associations |> List.tryFindIndex(fun a->Some a.Workspace=workspace) with
                | None->Error ["workspace-unassociated"]
                | Some index ->
                    let current=config.Associations[index]
                    let repositories=(current.Repositories |> List.filter(fun r->removals |> List.exists(fun x->String.Equals(x,r,StringComparison.OrdinalIgnoreCase)) |> not)) @ additions |> List.distinct
                    if repositories.IsEmpty then Error ["workspace-requires-repository"]
                    elif config.Associations |> List.mapi(fun i a->i,a) |> List.exists(fun (i,a)->i<>index && a.Repositories |> List.exists(fun r->repositories |> List.exists(fun x->String.Equals(x,r,StringComparison.OrdinalIgnoreCase)))) then Error ["association-conflict"]
                    else
                        let updated=config.Associations |> List.mapi(fun i a->if i=index then {a with Repositories=repositories} else a)
                        serialize path updated config.Retired;Ok "associated")
    let private pending association =
        match association.Destination with
        | Local root -> TelemetryStoreApplication.recoverReceiptCapacity root (TelemetryStoreApplication.assessProductionRoot root) |> Result.map(fun (_,count,_)->count)
        | Remote(_,_,spool) ->
            try Ok(if Directory.Exists spool then Directory.EnumerateFiles(spool,"*.ready") |> Seq.truncate 1 |> Seq.length |> int64 else 0L)
            with _->Error ["spool-unavailable"]
    let private cutover args =
        let path=configPath(option "--config" args)
        if not(File.Exists path) then Error ["unconfigured"] else
        withLock path (fun()->
            match load(Some path),option "--workspace" args,option "--producer" args,option "--stream" args,option "--to" args with
            | Ok config,Some workspace,Some producer,Some stream,Some destinationKind when [workspace;producer;stream]|>List.forall TelemetryReceipt.validId ->
                match config.Associations |> List.tryFind(fun a->a.Workspace=workspace) with
                | None->Error ["workspace-unassociated"]
                | Some old when (config.Associations@config.Retired) |> List.exists(fun a->a.Producer=producer)->Error ["identity-preserving-import-required"]
                | Some old->
                    match pending old with
                    | Error errors->Error errors
                    | Ok count when count<>0L->Error ["pending-old-destination"]
                    | Ok _->
                        let selected=
                            match destinationKind,option "--store-root" args,option "--endpoint" args,option "--credential-reference" args,option "--spool-root" args with
                            | "local",Some root,_,_,_ when Path.IsPathFullyQualified root->Some(Local(Path.GetFullPath root))
                            | "remote",_,Some endpoint,Some reference,Some spool when Path.IsPathFullyQualified spool && TelemetryReceipt.validId reference->
                                let uri=Uri endpoint
                                let remote:RemoteContract.ClientConfig={Endpoint=uri;CredentialReference=reference}
                                match RemoteContract.validateClientConfig remote with Ok _->Some(Remote(uri,reference,Path.GetFullPath spool))|Error _->None
                            | _->None
                        let selectedResult =
                            match selected with
                            | None->Error ["invalid destination"]
                            | Some(Local root as destination)->
                                let assessment=TelemetryStoreApplication.assessProductionRoot root
                                TelemetryStoreApplication.initialize root assessment |> Result.bind(fun _->TelemetryStoreApplication.enrollReceiptProducer root assessment {Workspace=workspace;Producer=producer;Stream=stream}) |> Result.map(fun _->destination)
                            | Some(Remote(_,reference,spool) as destination)->
                                if not((credential reference CancellationToken.None).GetAwaiter().GetResult().IsSome) then Error ["credential-unavailable"] else ensurePrivateDirectory spool |> Result.map(fun _->destination)
                        selectedResult |> Result.map(fun destination->
                            let replacement={old with Producer=producer;Stream=stream;Destination=destination}
                            let active=config.Associations |> List.map(fun a->if a.Workspace=workspace then replacement else a)
                            serialize path active (config.Retired@[old]);"cutover")
            | Error errors,_,_,_,_->Error errors
            | _->Error ["invalid cutover"])
    let run action args =
        let result=match action with
                   | "status"->status args
                   | "activate-local"->activate "local" args
                   | "activate-remote"->activate "remote" args
                   | "associate-repository"->associate args
                   | "cutover"->cutover args
                   | "submit"->match option "--input" args with Some path->tryPublishBound(option "--config" args)(option "--repository" args)(option "--producer" args)(option "--binding-digest" args)(File.ReadAllBytes path)|None->Error ["--input is required"]
                   | "drain"->tryDrain(option "--config" args)(option "--repository" args)
                   | _->Error ["unsupported workspace action"]
        match result with Ok value->Console.Out.Write value;green | Error errors->errors|>List.iter(fun e->Console.Error.WriteLine("fsgg-coord-engine: telemetry workspace: "+e));red
