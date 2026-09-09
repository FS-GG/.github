namespace FS.GG.Telemetry.Host

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Akka.Actor
open Akka.Configuration
open Akka.FSharp
open FS.GG.Coord
open FS.GG.Coord.Cli

type StoreConfig = { WorkspaceId:string; Root:string }
type CredentialConfig = { Reference:string; SecretFile:string; WorkspaceId:string; ProducerId:string; StreamId:string; Revoked:bool }
type HostConfig = { ListenUrl:string; CertificatePath:string; CertificatePasswordFile:string; ServiceLockPath:string; Stores:StoreConfig array; Credentials:CredentialConfig array }
type AuthEntry = { Scope:TelemetryReceipt.Scope; TokenHash:byte array; Revoked:bool }

module Configuration =
    let private privateRegularFile (path:string) =
        if not (Path.IsPathFullyQualified path) || not (File.Exists path) then Error "credential file is missing"
        else
            let info = FileInfo path
            if not (isNull info.LinkTarget) then Error "credential file must not be a symbolic link"
            elif not (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) then Error "host supports Linux x64 only"
            else
                let mode = File.GetUnixFileMode path
                let forbidden = UnixFileMode.GroupRead ||| UnixFileMode.GroupWrite ||| UnixFileMode.GroupExecute ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherWrite ||| UnixFileMode.OtherExecute
                if (mode &&& forbidden) <> enum 0 then Error "credential file permissions must be private" else Ok ()
    let validate config =
        let errors = ResizeArray<string>()
        if not (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) || RuntimeInformation.ProcessArchitecture<>Architecture.X64 then errors.Add "host supports Linux x64 only"
        match Uri.TryCreate(config.ListenUrl,UriKind.Absolute) with
        | true,u when u.Scheme=Uri.UriSchemeHttps && String.IsNullOrEmpty u.UserInfo && u.AbsolutePath="/" && String.IsNullOrEmpty u.Query && String.IsNullOrEmpty u.Fragment && not(config.ListenUrl.Contains ';') && config.ListenUrl=u.GetLeftPart(UriPartial.Authority) -> ()
        | _ -> errors.Add "listenUrl must be one HTTPS origin"
        if not (Path.IsPathFullyQualified config.CertificatePath) then errors.Add "certificatePath must be absolute"
        else match privateRegularFile config.CertificatePath with Error e->errors.Add e|Ok()->()
        match privateRegularFile config.CertificatePasswordFile with Error e->errors.Add e|Ok()->()
        if not (Path.IsPathFullyQualified config.ServiceLockPath) then errors.Add "serviceLockPath must be absolute"
        if config.Stores.Length=0 then errors.Add "at least one enrolled store is required"
        let roots = HashSet<string>(StringComparer.Ordinal)
        let workspaces = HashSet<string>(StringComparer.Ordinal)
        for store in config.Stores do
            if not (TelemetryReceipt.validId store.WorkspaceId) || not (Path.IsPathFullyQualified store.Root) then errors.Add "invalid store enrollment"
            if not (roots.Add(Path.GetFullPath store.Root)) || not (workspaces.Add store.WorkspaceId) then errors.Add "duplicate store enrollment"
        let producers = Dictionary<string,string>(StringComparer.Ordinal)
        let references = HashSet<string>(StringComparer.Ordinal)
        let tokens = Dictionary<string,TelemetryReceipt.Scope>(StringComparer.Ordinal)
        for credential in config.Credentials do
            if not (TelemetryReceipt.validId credential.WorkspaceId && TelemetryReceipt.validId credential.ProducerId && TelemetryReceipt.validId credential.StreamId) then errors.Add "invalid credential scope"
            if String.IsNullOrWhiteSpace credential.Reference || credential.Reference.Length>256 || credential.Reference |> Seq.exists(fun c -> not(Char.IsAsciiLetterOrDigit c || c='.' || c='_' || c='-')) then errors.Add "invalid credential reference"
            elif not (references.Add credential.Reference) then errors.Add "duplicate credential reference"
            match producers.TryGetValue credential.ProducerId with | true,w when w<>credential.WorkspaceId -> errors.Add "producer is bound to multiple workspaces" | false,_ -> producers[credential.ProducerId] <- credential.WorkspaceId | _ -> ()
            if not (workspaces.Contains credential.WorkspaceId) then errors.Add "credential names an unenrolled workspace"
            match privateRegularFile credential.SecretFile with
            | Error e -> errors.Add e
            | Ok () ->
                let token=File.ReadAllText(credential.SecretFile).Trim()
                let scope:TelemetryReceipt.Scope={Workspace=credential.WorkspaceId;Producer=credential.ProducerId;Stream=credential.StreamId}
                match tokens.TryGetValue token with | true,prior when prior<>scope -> errors.Add "credential secret is assigned to incompatible scopes" | false,_ -> tokens[token]<-scope | _ -> ()
        if producers.Count > 128 then errors.Add "host producer capacity exceeds 128"
        if errors.Count=0 then Ok config else Error(List.ofSeq errors)
    let load (path:string) =
        try
            if not (Path.IsPathFullyQualified path) then Error [ "host config path must be absolute" ] else
            match privateRegularFile path with
            | Error error -> Error [error]
            | Ok () ->
                let bytes=File.ReadAllBytes path
                if bytes.Length>65536 then Error ["host configuration is oversized"] else
                use document=JsonDocument.Parse(bytes,JsonDocumentOptions(AllowTrailingCommas=false,CommentHandling=JsonCommentHandling.Disallow,MaxDepth=8))
                let closed (element:JsonElement) expected =
                    let names=element.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                    names.Length=Set.count expected && Array.distinct names |> Array.length = names.Length && Set.ofArray names=expected
                let top=set["ListenUrl";"CertificatePath";"CertificatePasswordFile";"ServiceLockPath";"Stores";"Credentials"]
                let store=set["WorkspaceId";"Root"]
                let credential=set["Reference";"SecretFile";"WorkspaceId";"ProducerId";"StreamId";"Revoked"]
                if not(closed document.RootElement top) || document.RootElement.GetProperty("Stores").EnumerateArray() |> Seq.exists(fun x->not(closed x store)) || document.RootElement.GetProperty("Credentials").EnumerateArray() |> Seq.exists(fun x->not(closed x credential)) then Error ["host configuration schema is invalid"]
                else
                    let options = JsonSerializerOptions(PropertyNameCaseInsensitive=false,UnmappedMemberHandling=Serialization.JsonUnmappedMemberHandling.Disallow)
                    JsonSerializer.Deserialize<HostConfig>(bytes,options) |> validate
        with _ -> Error [ "invalid host configuration" ]
    let credentials config =
        config.Credentials |> Array.map (fun c ->
            let token = File.ReadAllText(c.SecretFile).Trim()
            if token.Length < 32 || token.Length > 4096 then invalidOp "credential secret length is invalid"
            c.Reference,{ Scope={Workspace=c.WorkspaceId;Producer=c.ProducerId;Stream=c.StreamId}; TokenHash=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes token); Revoked=c.Revoked }) |> Map.ofArray

type private Command = Submit of TelemetryReceipt.Scope * byte array | Lookup of TelemetryReceipt.Scope * string | Drain
type Reply = { Status:int; Body:byte array }

module Capacity =
    let admitsNewIdentity lifetime pending pendingBytes incomingBytes = lifetime<1000000L && pending<1024L && pendingBytes+incomingBytes<=64L*1024L*1024L

module Runtime =
    module private Native =
        [<DllImport("libc",SetLastError=true)>]
        extern int flock(int fd,int operation)
    [<Sealed>]
    type ServiceLock private (stream:FileStream) =
        interface IDisposable with member _.Dispose() = Native.flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(),8) |> ignore; stream.Dispose()
        static member Acquire(path:string) =
            try
                let stream=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)
                if Native.flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(),2 ||| 4)<>0 then stream.Dispose(); Error "service already running"
                else Ok(new ServiceLock(stream))
            with :? IOException -> Error "service lock unavailable"
    [<Sealed>]
    type HostState(config:HostConfig, assessmentFor:string->TelemetryStore.DurabilityAssessment) =
        let stores = config.Stores |> Array.map(fun s -> s.WorkspaceId,s.Root) |> Map.ofArray
        let mutable ready = false
        let akkaConfig = ConfigurationFactory.ParseString("blocking-io-dispatcher {\n type = Dispatcher\n executor = thread-pool-executor\n throughput = 1\n thread-pool-executor { fixed-pool-size = 4 }\n}\n")
        let system = ActorSystem.Create("fsgg-telemetry",akkaConfig)
        let actor =
            spawnOpt system "admission" (fun mailbox ->
                let approved root = assessmentFor root
                let error code status = {Status=status;Body=FS.GG.Telemetry.RemoteContract.writeError code}
                let aggregate () = stores |> Seq.map(fun kv -> TelemetryStoreApplication.recoverReceiptCapacity kv.Value (approved kv.Value)) |> Seq.fold(fun state value -> match state,value with Ok(a,b,c),Ok(x,y,z)->Ok(a+x,b+y,c+z)|_->Error()) (Ok(0L,0L,0L))
                let rec loop () = actor {
                    let! command=mailbox.Receive()
                    match command with
                    | Submit(scope,body) ->
                        let reply =
                            match stores.TryFind scope.Workspace,TelemetryReceipt.parse body with
                            | None,_ -> error "unauthorized-scope" 403
                            | _,Error errors -> let code=errors|>List.tryFind FS.GG.Telemetry.RemoteContract.validErrorCode|>Option.defaultValue "invalid-request" in error code 400
                            | Some root,Ok envelope ->
                                match TelemetryReceipt.authorize scope envelope with
                                | Error _ -> error "unauthorized-scope" 403
                                | Ok () ->
                                match TelemetryStoreApplication.lookupReceipt root (approved root) scope envelope.BatchId with
                                | Ok json -> match FS.GG.Telemetry.RemoteContract.parseReceipt(System.Text.Encoding.UTF8.GetBytes json) with Ok receipt when receipt.Digest=envelope.Digest -> {Status=200;Body=System.Text.Encoding.UTF8.GetBytes json} | Ok _ -> error "identity-conflict" 409 | Error _ -> error "storage-unavailable" 503
                                | Error ["receipt-unavailable"] ->
                                    match aggregate() with
                                    | Ok(lifetime,pending,bytes) when Capacity.admitsNewIdentity lifetime pending bytes (int64(System.Text.Encoding.UTF8.GetByteCount envelope.Canonical)) -> match TelemetryStoreApplication.submitReceipt root (approved root) scope body with Ok json->{Status=202;Body=System.Text.Encoding.UTF8.GetBytes json}|Error errors->let code=errors|>List.tryFind FS.GG.Telemetry.RemoteContract.validErrorCode|>Option.defaultValue "storage-unavailable" in error code (if code="identity-conflict" then 409 elif code="overload" then 429 else 503)
                                    | Ok _ -> error "overload" 429
                                    | Error _ -> error "storage-unavailable" 503
                                | Error errors -> let code=errors|>List.tryFind FS.GG.Telemetry.RemoteContract.validErrorCode|>Option.defaultValue "storage-unavailable" in error code (if code="unauthorized-scope" then 403 else 503)
                        mailbox.Sender() <! reply
                    | Lookup(scope,batch) ->
                        let reply=match stores.TryFind scope.Workspace with None->error "unauthorized-scope" 403|Some root->match TelemetryStoreApplication.lookupReceipt root (approved root) scope batch with Ok json->{Status=200;Body=System.Text.Encoding.UTF8.GetBytes json}|Error errors->let code=errors|>List.tryFind FS.GG.Telemetry.RemoteContract.validErrorCode|>Option.defaultValue "storage-unavailable" in error code (if code="unauthorized-scope" then 403 elif code="invalid-request" then 400 elif code="receipt-unavailable" then 404 else 503)
                        mailbox.Sender() <! reply
                    | Drain ->
                        let results=stores |> Seq.map(fun kv->TelemetryStoreApplication.drainReceipts kv.Value (approved kv.Value) kv.Key) |> Seq.toArray
                        if results |> Array.exists Result.isError then ready<-false
                        system.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds 1.,mailbox.Self,Drain,mailbox.Self)
                    return! loop() }
                loop()) [ SpawnOption.Dispatcher "blocking-io-dispatcher" ]
        let slots = new SemaphoreSlim(16,16)
        member _.Actor=actor
        member _.Slots=slots
        member _.TryAcquireSlot()=slots.Wait(0)
        member _.ReleaseSlot()=slots.Release() |> ignore
        member _.Ready with get()=ready and set value=ready<-value
        member _.StartDrain() = actor.Tell Drain
        interface IDisposable with member _.Dispose() = system.Terminate().GetAwaiter().GetResult(); slots.Dispose()
        new(config:HostConfig) = new HostState(config,TelemetryStoreApplication.assessProductionRoot)
    let authenticate (entries:Map<string,AuthEntry>) (token:string) =
        let candidate=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes token)
        entries |> Seq.tryPick(fun kv -> let entry=kv.Value in if not entry.Revoked && CryptographicOperations.FixedTimeEquals(ReadOnlySpan<byte>(entry.TokenHash),ReadOnlySpan<byte>(candidate)) then Some entry.Scope else None)
    let recover config =
        config.Stores |> Array.fold (fun state store ->
            match state with
            | Error e -> Error e
            | Ok () ->
                let assessment=TelemetryStoreApplication.assessProductionRoot store.Root
                match TelemetryStoreApplication.status store.Root assessment with
                | Ok _ -> match TelemetryStoreApplication.drainReceipts store.Root assessment store.WorkspaceId with Ok _ -> Ok() | Error e -> Error e
                | Error e -> Error e) (Ok())
    let private askAcquired (state:HostState) (message:Command) (cancellationToken:CancellationToken) = task {
            let completion=state.Actor.Ask<Reply>(message,Timeout.InfiniteTimeSpan,CancellationToken.None)
            completion.ContinueWith(fun (_:Task<Reply>) -> state.ReleaseSlot()) |> ignore
            let! winner=Task.WhenAny(completion,Task.Delay(TimeSpan.FromSeconds 10.,cancellationToken))
            if Object.ReferenceEquals(winner,completion) then
                try return completion.Result with _ -> return {Status=503;Body=FS.GG.Telemetry.RemoteContract.writeError "storage-unavailable"}
            else return {Status=503;Body=FS.GG.Telemetry.RemoteContract.writeError "receipt-unavailable"} }
    let submitAcquired state scope bytes ct = askAcquired state (Submit(scope,bytes)) ct
    let lookupAcquired state scope batch ct = askAcquired state (Lookup(scope,batch)) ct
    let submit (state:HostState) scope bytes ct = if state.TryAcquireSlot() then submitAcquired state scope bytes ct else Task.FromResult({Status=429;Body=FS.GG.Telemetry.RemoteContract.writeError "overload"})
    let lookup (state:HostState) scope batch ct = if state.TryAcquireSlot() then lookupAcquired state scope batch ct else Task.FromResult({Status=429;Body=FS.GG.Telemetry.RemoteContract.writeError "overload"})
