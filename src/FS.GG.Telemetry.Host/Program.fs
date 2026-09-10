namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open FS.GG.Coord.Cli
open FS.GG.Telemetry.Dashboard

module private BrowserComposition =
    let options config =
        { PublicOrigin=Uri config.ListenUrl
          IdleLifetime=TimeSpan.FromSeconds(float config.BrowserSession.IdleSeconds)
          AbsoluteLifetime=TimeSpan.FromSeconds(float config.BrowserSession.AbsoluteSeconds)
          MaximumSessions=config.BrowserSession.MaximumSessions
          LoginAttemptsPerMinute=config.BrowserSession.LoginAttemptsPerMinute
          LoginAdmission=config.BrowserSession.LoginAdmission
          QueryAdmission=config.BrowserSession.QueryAdmission
          QueryTimeout=TimeSpan.FromSeconds(float config.BrowserSession.QueryTimeoutSeconds) }
    let snapshot config workspace itemId =
        match config.Stores |> Array.tryFind(fun store->store.WorkspaceId=workspace) with
        | None -> Error ["projection-unavailable"]
        | Some store ->
            match TelemetryStoreApplication.scopedDashboardSnapshot store.Root (TelemetryStoreApplication.assessProductionRoot store.Root) workspace itemId with
            | Error _ -> Error ["projection-unavailable"]
            | Ok envelope ->
                match DashboardProjection.project workspace (Encoding.UTF8.GetBytes envelope) with
                | Ok bytes -> Ok bytes
                | Error _ -> Error ["projection-unavailable"]

module Hosting =
    let createBuilder (listenUrl:string) certificate =
        let builder=WebApplication.CreateBuilder(WebApplicationOptions(Args=[||]))
        builder.Configuration.Sources.Clear()
        builder.Configuration.AddInMemoryCollection() |> ignore
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.UseUrls(listenUrl) |> ignore
        builder.WebHost.ConfigureKestrel(fun options -> options.ConfigureHttpsDefaults(fun https -> https.ServerCertificate <- certificate)) |> ignore
        builder

module Endpoints =
    let private unauthorized (context:HttpContext) = task { context.Response.StatusCode<-401; do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "unauthorized-scope") }
    let configure (app:WebApplication) (state:Runtime.HostState) credentials =
        let auth (context:HttpContext) =
            let authorization=string context.Request.Headers.Authorization
            if authorization.StartsWith("Bearer ",StringComparison.Ordinal) then Runtime.authenticate credentials (authorization.Substring 7) else None
        app.MapGet("/private/health",Func<HttpContext,Task>(fun context -> task {
            match auth context with
            | Some _ ->
                context.Response.StatusCode <- if state.Ready then 200 else 503
                do! context.Response.WriteAsJsonAsync({|status=if state.Ready then "ready" else "recovering"|})
            | None -> do! unauthorized context })) |> ignore
        app.MapPost("/v1/batches",Func<HttpContext,Task>(fun context -> task {
            match auth context with
            | None -> do! unauthorized context
            | Some scope ->
                if not(state.TryAcquireSlot()) then context.Response.StatusCode<-429; do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "overload")
                else
                    let mutable transferred=false
                    try
                        try
                            if context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > int64 FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes then
                                context.Response.StatusCode<-413
                                do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "oversized-batch")
                            else
                                use deadline=Threading.CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted)
                                deadline.CancelAfter(TimeSpan.FromSeconds 10.)
                                use stream=new MemoryStream()
                                let buffer=Array.zeroCreate<byte> 8192
                                let mutable total=0
                                let mutable more=true
                                while more && total <= FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes do let! count=context.Request.Body.ReadAsync(buffer,deadline.Token) in if count=0 then more<-false else stream.Write(buffer,0,count); total<-total+count
                                if total>FS.GG.Coord.TelemetryReceipt.MaxEnvelopeBytes then
                                    context.Response.StatusCode<-413
                                    do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "oversized-batch")
                                else
                                    transferred<-true
                                    let! reply=Runtime.submitAcquired state scope (stream.ToArray()) deadline.Token
                                    context.Response.StatusCode<-reply.Status
                                    context.Response.ContentType<-"application/json"
                                    do! context.Response.Body.WriteAsync(reply.Body)
                        with :? OperationCanceledException when not transferred && not context.Response.HasStarted -> context.Response.StatusCode<-408
                    finally if not transferred then state.ReleaseSlot() })) |> ignore
        app.MapGet("/v1/receipts/{batch}",Func<HttpContext,string,Task>(fun context batch -> task {
            match auth context with
            | None -> do! unauthorized context
            | Some scope ->
                if not(state.TryAcquireSlot()) then context.Response.StatusCode<-429; do! context.Response.Body.WriteAsync(FS.GG.Telemetry.RemoteContract.writeError "overload")
                else
                    // From this point the actor completion owns the admission slot, even if the client disconnects.
                    let! reply=Runtime.lookupAcquired state scope batch context.RequestAborted
                    context.Response.StatusCode<-reply.Status
                    context.Response.ContentType<-"application/json"
                    do! context.Response.Body.WriteAsync(reply.Body) })) |> ignore

module Operations =
    module private Native =
        [<DllImport("libc",EntryPoint="open",SetLastError=true)>]
        extern int openDirectory(string path,int flags)
        [<DllImport("libc",SetLastError=true)>]
        extern int fsync(int descriptor)
        [<DllImport("libc",SetLastError=true)>]
        extern int close(int descriptor)
    let private syncDirectory path =
        let descriptor=Native.openDirectory(path,0x10000 ||| 0x80000)
        if descriptor<0 then raise(IOException "directory sync unavailable")
        try if Native.fsync descriptor<>0 then raise(IOException "directory sync unavailable") finally Native.close descriptor |> ignore
    let private digestBytes (bytes:byte array)=Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let private digestFile path=use stream=File.OpenRead path in Convert.ToHexString(SHA256.HashData stream).ToLowerInvariant()
    let private flushFile path=use stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read,4096,FileOptions.WriteThrough) in stream.Flush true
    let private isLowerSha256 (value:string) =
        not(isNull value) && value.Length=64 && value |> Seq.forall(fun c->Char.IsAsciiHexDigit c && not(Char.IsLetter c && Char.IsUpper c))
    let private hasSymlinkAncestor path =
        let mutable current=Path.GetFullPath path
        let mutable found=false
        while not found && not(String.IsNullOrEmpty current) do
            if Directory.Exists current then found <- not(isNull(DirectoryInfo(current).LinkTarget))
            elif File.Exists current then found <- not(isNull(FileInfo(current).LinkTarget))
            let parent=Path.GetDirectoryName current
            current <- if parent=current then null else parent
        found
    let private configMetadataDigest config =
        // Bind logical enrollment and authorization without binding machine-specific store paths.
        // A restore is always published below a new state root and activated through a reviewed config.
        JsonSerializer.SerializeToUtf8Bytes {|schema=config.Schema;listenUrl=config.ListenUrl;workspaces=config.Stores |> Array.map _.WorkspaceId;credentials=config.Credentials |> Array.map(fun x->{|reference=x.Reference;workspaceId=x.WorkspaceId;producerId=x.ProducerId;streamId=x.StreamId;revoked=x.Revoked|});browserPrincipals=config.BrowserPrincipals |> Array.map(fun x->{|principalId=x.PrincipalId;workspaceIds=x.WorkspaceIds;revoked=x.Revoked|});browserSession=config.BrowserSession|} |> digestBytes
    let private freeSpaceFor path =
        try
            let mutable candidate=Path.GetFullPath path
            while not(Directory.Exists candidate) && not(String.IsNullOrEmpty candidate) do candidate<-Path.GetDirectoryName candidate
            DriveInfo.GetDrives()
            |> Array.filter(fun drive -> candidate.StartsWith(Path.GetFullPath drive.RootDirectory.FullName,StringComparison.Ordinal))
            |> Array.sortByDescending(fun drive->drive.RootDirectory.FullName.Length)
            |> Array.tryHead
            |> Option.filter _.IsReady
            |> Option.map _.AvailableFreeSpace
        with _ -> None
    let private writeError code errors =
        let body=JsonSerializer.Serialize {| schema="fsgg.telemetry.host-error/1"; code=code; errors=errors |> List.map(fun _->code) |> List.toArray |}
        Console.Error.WriteLine body
    let private resultExit fallback (result:Result<string,string list>) =
        match result with
        | Ok (json:string) -> Console.Out.Write json; 0
        | Error errors -> writeError fallback errors; if fallback="backup-integrity-failed" then 5 elif fallback="invalid-configuration" then 2 else 3
    let private load path = Configuration.load path
    let private withLock config action =
        match Runtime.ServiceLock.Acquire config.ServiceLockPath with
        | Error _ -> writeError "service-lock-unavailable" ["locked"]; 4
        | Ok serviceLock -> use serviceLock=serviceLock in action()
    let private storeFor config workspace = config.Stores |> Array.tryFind(fun store->store.WorkspaceId=workspace)
    let private preflight config assessmentFor =
        try
            if config.BrowserPrincipals.Length=0 then Error ["dashboard-auth-unavailable"] else
            Configuration.credentials config |> ignore
            Configuration.browserKeyHashes config |> ignore
            use _browser=new BrowserSecurity.Service(BrowserComposition.options config,config.BrowserPrincipals)
            use _certificate=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath,File.ReadAllText(config.CertificatePasswordFile).Trim())
            let failures=
                config.Stores
                |> Array.toList
                |> List.collect(fun store ->
                    let assessment=assessmentFor store.Root
                    let free = freeSpaceFor store.Root
                    [ match TelemetryStoreApplication.status store.Root assessment with Error errors -> yield! errors | Ok _ -> ()
                      match TelemetryStoreApplication.scopedDashboardSnapshot store.Root assessment store.WorkspaceId None with Error errors -> yield! errors | Ok _ -> ()
                      if free |> Option.forall(fun bytes->bytes<512L*1024L*1024L) then yield "capacity-reserve-unavailable" ])
            if failures.IsEmpty then Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.host-preflight/1"; status="ready"; stores=config.Stores.Length; dashboardAuthentication="ready"; supportedStoreSchemaMin=9; supportedStoreSchemaMax=9 |}+"\n") else Error failures
        with _ -> Error ["invalid-configuration"]
    let private status config assessmentFor =
        let stores =
            config.Stores |> Array.map(fun store ->
                let assessment=assessmentFor store.Root
                let state=match TelemetryStoreApplication.status store.Root assessment with Ok _->"ready"|Error _->"unavailable"
                match TelemetryStoreApplication.receiptCapacity store.Root assessment with
                | Ok(lifetime,pending,pendingBytes) -> {|workspaceId=store.WorkspaceId;status=state;capacity="available";lifetimeReceipts=Some lifetime;pendingReceipts=Some pending;pendingBytes=Some pendingBytes|}
                | Error _ -> {|workspaceId=store.WorkspaceId;status=state;capacity="unavailable";lifetimeReceipts=None;pendingReceipts=None;pendingBytes=None|})
        let processState=match Runtime.ServiceLock.Probe config.ServiceLockPath with Ok true->"running"|Ok false->"stopped"|Error _->"unknown"
        let dashboardState=if config.BrowserPrincipals.Length>0 then "configured" else "unavailable"
        Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.host-status/1"; ``process`` = processState; recovery="unknown"; dashboardAuthentication=dashboardState; supportedStoreSchemaMin=9; supportedStoreSchemaMax=9; stores=stores |}+"\n")
    let runWithAssessment (argv:string array) assessmentFor =
        match List.ofArray argv with
        | ["init";"--root";root;"--workspace";workspace] ->
            match TelemetryStoreApplication.initialize root (assessmentFor root) with
            | Error errors -> resultExit "storage-unavailable" (Error errors)
            | Ok _ -> TelemetryStoreApplication.provisionReceiptWorkspace root (assessmentFor root) workspace |> resultExit "storage-unavailable"
        | ["enroll-producer";"--config";path;"--reference";reference;"--secret-file";secret;"--workspace";workspace;"--producer";producer;"--stream";stream]
        | ["enroll-producer";"--config";path;"--reference";reference;"--secret-file";secret;"--workspace";workspace;"--producer";producer;"--stream";stream;"--revoked"] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () ->
                let revoked=argv.Length=14
                match config.Credentials |> Array.tryFind(fun entry->entry.Reference=reference && entry.SecretFile=secret && entry.WorkspaceId=workspace && entry.ProducerId=producer && entry.StreamId=stream && entry.Revoked=revoked),storeFor config workspace with
                | Some _,Some store ->
                    match TelemetryStoreApplication.provisionReceiptWorkspace store.Root (assessmentFor store.Root) workspace with
                    | Error errors -> resultExit "storage-unavailable" (Error errors)
                    | Ok _ -> TelemetryStoreApplication.enrollReceiptProducer store.Root (assessmentFor store.Root) {Workspace=workspace;Producer=producer;Stream=stream} |> resultExit "storage-unavailable"
                | _ -> resultExit "invalid-configuration" (Error ["enrollment is not declared by config"]))
        | ["preflight";"--config";path] ->
            match load path with Error errors->resultExit "invalid-configuration" (Error errors)|Ok config->withLock config (fun()->preflight config assessmentFor |> resultExit "preflight-failed")
        | ["status";"--config";path] -> match load path with Error errors->resultExit "invalid-configuration" (Error errors)|Ok config->status config assessmentFor |> resultExit "status-unavailable"
        | ["backup";"--config";path;"--output";output] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () ->
                try
                    let outputParent=if Path.IsPathFullyQualified output then Path.GetDirectoryName(Path.GetFullPath output) else null
                    if not(Path.IsPathFullyQualified output) || isNull outputParent || not(Directory.Exists outputParent) || hasSymlinkAncestor output || Directory.Exists output || File.Exists output then resultExit "backup-integrity-failed" (Error ["backup output must be a fresh child of an existing real directory"]) else
                    let temporary=output+".tmp-"+Guid.NewGuid().ToString("N")
                    try
                        Directory.CreateDirectory temporary |> ignore
                        if not(OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                        let mutable failure=None
                        for store in config.Stores do if failure.IsNone then match TelemetryStoreApplication.backupReceiptStore store.Root (assessmentFor store.Root) store.WorkspaceId (Path.Combine(temporary,store.WorkspaceId)) with Ok _->()|Error errors->failure<-Some errors
                        match failure with
                        | Some errors -> resultExit "backup-integrity-failed" (Error errors)
                        | None ->
                            let workspaces=config.Stores |> Array.sortBy _.WorkspaceId |> Array.map(fun store->{|workspaceId=store.WorkspaceId;path=store.WorkspaceId;manifestSha256=digestFile(Path.Combine(temporary,store.WorkspaceId,"manifest.json"))|})
                            let manifest=JsonSerializer.Serialize {|schema="fsgg.telemetry.host-backup-set/1";hostVersion="0.1.0";supportedStoreSchemaMin=9;supportedStoreSchemaMax=9;configMetadataSha256=configMetadataDigest config;createdAt=DateTimeOffset.UtcNow.ToString("O");workspaces=workspaces|}+"\n"
                            let manifestPath=Path.Combine(temporary,"backup-manifest.json")
                            File.WriteAllText(manifestPath,manifest,UTF8Encoding(false));flushFile manifestPath;syncDirectory temporary
                            Directory.Move(temporary,output);syncDirectory(Path.GetDirectoryName output)
                            Console.Out.Write(JsonSerializer.Serialize {|schema="fsgg.telemetry.host-backup-result/1";output=output;manifestSha256=digestFile(Path.Combine(output,"backup-manifest.json"))|}+"\n");0
                    finally if Directory.Exists temporary then Directory.Delete(temporary,true)
                with _ -> resultExit "backup-integrity-failed" (Error ["backup creation failed"]))
        | ["restore";"--config";path;"--input";input;"--state-root";stateRoot] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () ->
                try
                    if not(Path.IsPathFullyQualified input) || not(Directory.Exists input) || hasSymlinkAncestor input || not(Path.IsPathFullyQualified stateRoot) || Directory.Exists stateRoot || File.Exists stateRoot || hasSymlinkAncestor stateRoot then resultExit "backup-integrity-failed" (Error ["restore paths are invalid"]) else
                    let temporary=stateRoot+".tmp-"+Guid.NewGuid().ToString("N")
                    try
                        let manifestPath=Path.Combine(input,"backup-manifest.json")
                        if not(File.Exists manifestPath) || FileInfo(manifestPath).Length>1024L*1024L then resultExit "backup-integrity-failed" (Error ["backup manifest unavailable"]) else
                        let manifestBytes=File.ReadAllBytes manifestPath
                        use document=JsonDocument.Parse manifestBytes
                        let root=document.RootElement
                        let names=root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                        let frozenManifestDigest=digestBytes manifestBytes
                        let createdAt=root.GetProperty("createdAt").GetString()
                        let mutable parsedCreatedAt=DateTimeOffset.MinValue
                        let declared=root.GetProperty("workspaces").EnumerateArray() |> Seq.map(fun entry->
                            let entryNames=entry.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                            if entryNames.Length<>3 || Array.distinct entryNames |> Array.length<>3 || Set.ofArray entryNames<>set["workspaceId";"path";"manifestSha256"] then invalidOp "invalid workspace manifest entry"
                            entry.GetProperty("workspaceId").GetString(),entry.GetProperty("path").GetString(),entry.GetProperty("manifestSha256").GetString()) |> Seq.truncate 129 |> Seq.toArray
                        let expected=config.Stores |> Array.map _.WorkspaceId |> Array.sort
                        let actual=declared |> Array.map(fun (workspace,_,_)->workspace) |> Array.sort
                        let actualEntries=Directory.EnumerateFileSystemEntries(input,"*",SearchOption.TopDirectoryOnly) |> Seq.truncate 131 |> Seq.toArray
                        let actualNames=actualEntries |> Array.map Path.GetFileName |> Array.sort
                        let expectedNames=Array.append [|"backup-manifest.json"|] expected |> Array.sort
                        let schemaMin=root.GetProperty("supportedStoreSchemaMin").GetInt32()
                        let schemaMax=root.GetProperty("supportedStoreSchemaMax").GetInt32()
                        let invalidManifest = names.Length<>7 || Array.distinct names|>Array.length<>7 || Set.ofArray names<>set["schema";"hostVersion";"supportedStoreSchemaMin";"supportedStoreSchemaMax";"configMetadataSha256";"createdAt";"workspaces"] || root.GetProperty("schema").GetString()<>"fsgg.telemetry.host-backup-set/1" || String.IsNullOrWhiteSpace(root.GetProperty("hostVersion").GetString()) || root.GetProperty("configMetadataSha256").GetString()<>configMetadataDigest config || isNull createdAt || not(DateTimeOffset.TryParse(createdAt,Globalization.CultureInfo.InvariantCulture,Globalization.DateTimeStyles.RoundtripKind,&parsedCreatedAt)) || declared.Length<>expected.Length || actual<>expected || Array.distinct actual |> Array.length<>actual.Length || actualNames<>expectedNames || actualEntries |> Array.exists(fun entry->if Directory.Exists entry then not(isNull(DirectoryInfo(entry).LinkTarget)) else not(isNull(FileInfo(entry).LinkTarget))) || declared |> Array.exists(fun (workspace,relative,digest)->isNull workspace || not(FS.GG.Coord.TelemetryReceipt.validId workspace) || relative<>workspace || not(isLowerSha256 digest) || digestFile(Path.Combine(input,relative,"manifest.json"))<>digest)
                        if invalidManifest then resultExit "backup-integrity-failed" (Error ["backup manifest invalid"])
                        elif schemaMin>9 || schemaMax<9 then resultExit "restore-incompatible" (Error ["backup schema is incompatible"])
                        else
                            Directory.CreateDirectory temporary |> ignore
                            if not(OperatingSystem.IsWindows()) then File.SetUnixFileMode(temporary,UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute)
                            let mutable failure=None
                            for store in config.Stores do
                                if failure.IsNone then
                                    match TelemetryStoreApplication.restoreReceiptStore (Path.Combine(input,store.WorkspaceId)) (Path.Combine(temporary,store.WorkspaceId)) (assessmentFor stateRoot) store.WorkspaceId with
                                    | Ok _ ->
                                        let _,_,declaredDigest=declared |> Array.find(fun (workspace,_,_)->workspace=store.WorkspaceId)
                                        if digestFile(Path.Combine(input,store.WorkspaceId,"manifest.json"))<>declaredDigest then failure<-Some ["backup input changed during restore"]
                                    | Error errors->failure<-Some errors
                            match failure with
                            | Some ["backup-incompatible"] -> resultExit "restore-incompatible" (Error ["backup schema is incompatible"])
                            | Some errors -> resultExit "backup-integrity-failed" (Error errors)
                            | None when digestFile manifestPath<>frozenManifestDigest -> resultExit "backup-integrity-failed" (Error ["backup input changed during restore"])
                            | None -> syncDirectory temporary;Directory.Move(temporary,stateRoot);syncDirectory(Path.GetDirectoryName stateRoot);Console.Out.Write(JsonSerializer.Serialize {|schema="fsgg.telemetry.host-restore-set/1";root=stateRoot;sourceManifestSha256=frozenManifestDigest|}+"\n");0
                    finally if Directory.Exists temporary then Directory.Delete(temporary,true)
                with _ -> resultExit "backup-integrity-failed" (Error ["backup restore failed"]))
        | _ -> writeError "usage" ["invalid command"];2
    let run argv=runWithAssessment argv TelemetryStoreApplication.assessProductionRoot

module Program =
    [<EntryPoint>]
    let main argv =
        let servePath =
            match List.ofArray argv with
            | ["serve";"--config";path] -> Some path
            | [path] -> Some path
            | _ -> None
        match servePath with
        | None -> Operations.run argv
        | Some path ->
        match Configuration.load path with
        | Error _ -> 2
        | Ok config when config.BrowserPrincipals.Length=0 -> 2
        | Ok config ->
            try
                match Runtime.ServiceLock.Acquire config.ServiceLockPath with
                | Error _ -> 4
                | Ok serviceLock ->
                  use serviceLock=serviceLock
                  let credentials=Configuration.credentials config
                  let browserOptions=BrowserComposition.options config
                  use browserSecurity=new BrowserSecurity.Service(browserOptions,config.BrowserPrincipals)
                  match Runtime.recover config with
                  | Error _ -> 3
                  | Ok () when config.Stores |> Array.exists(fun store->BrowserComposition.snapshot config store.WorkspaceId None |> Result.isError) -> 3
                  | Ok () ->
                    use state=new Runtime.HostState(config)
                    state.Ready<-true; state.StartDrain()
                    use certificate=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath,File.ReadAllText(config.CertificatePasswordFile).Trim())
                    let builder=Hosting.createBuilder config.ListenUrl certificate
                    let app=builder.Build()
                    Endpoints.configure app state credentials
                    BrowserEndpoints.map app browserOptions browserSecurity (BrowserComposition.snapshot config)
                    app.Run(); 0
            with :? IOException -> 4
