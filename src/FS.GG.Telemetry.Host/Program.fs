namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open FS.GG.Coord.Cli

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
            use _certificate=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath,File.ReadAllText(config.CertificatePasswordFile).Trim())
            let failures=
                config.Stores
                |> Array.toList
                |> List.collect(fun store ->
                    let assessment=assessmentFor store.Root
                    let free = try DriveInfo(Path.GetPathRoot store.Root).AvailableFreeSpace with _ -> 0L
                    [ match TelemetryStoreApplication.status store.Root assessment with Error errors -> yield! errors | Ok _ -> ()
                      match TelemetryStoreApplication.scopedDashboardSnapshot store.Root assessment store.WorkspaceId None with Error errors -> yield! errors | Ok _ -> ()
                      if free<512L*1024L*1024L then yield "capacity-reserve-unavailable" ])
            if failures.IsEmpty then Ok(JsonSerializer.Serialize {| schema="fsgg.telemetry.host-preflight/1"; status="ready"; stores=config.Stores.Length; dashboardAuthentication="ready"; supportedStoreSchemaMin=9; supportedStoreSchemaMax=9 |}+"\n") else Error failures
        with _ -> Error ["invalid-configuration"]
    let private status config assessmentFor =
        let stores =
            config.Stores |> Array.map(fun store ->
                let assessment=assessmentFor store.Root
                let state=match TelemetryStoreApplication.status store.Root assessment with Ok _->"ready"|Error _->"unavailable"
                let lifetime,pending,pendingBytes=match TelemetryStoreApplication.receiptCapacity store.Root assessment with Ok values->values|Error _->0L,0L,0L
                {|workspaceId=store.WorkspaceId;status=state;lifetimeReceipts=lifetime;pendingReceipts=pending;pendingBytes=pendingBytes|})
        let running=
            match Runtime.ServiceLock.Acquire config.ServiceLockPath with
            | Ok serviceLock -> (serviceLock:>IDisposable).Dispose(); false
            | Error _ -> true
        let processState=if running then "running" else "stopped"
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
            | Ok config ->
                let revoked=argv.Length=14
                match config.Credentials |> Array.tryFind(fun entry->entry.Reference=reference && entry.SecretFile=secret && entry.WorkspaceId=workspace && entry.ProducerId=producer && entry.StreamId=stream && entry.Revoked=revoked),storeFor config workspace with
                | Some _,Some store ->
                    match TelemetryStoreApplication.provisionReceiptWorkspace store.Root (assessmentFor store.Root) workspace with
                    | Error errors -> resultExit "storage-unavailable" (Error errors)
                    | Ok _ -> TelemetryStoreApplication.enrollReceiptProducer store.Root (assessmentFor store.Root) {Workspace=workspace;Producer=producer;Stream=stream} |> resultExit "storage-unavailable"
                | _ -> resultExit "invalid-configuration" (Error ["enrollment is not declared by config"])
        | ["preflight";"--config";path] ->
            match load path with Error errors->resultExit "invalid-configuration" (Error errors)|Ok config->withLock config (fun()->preflight config assessmentFor |> resultExit "preflight-failed")
        | ["status";"--config";path] -> match load path with Error errors->resultExit "invalid-configuration" (Error errors)|Ok config->status config assessmentFor |> resultExit "status-unavailable"
        | ["backup";"--config";path;"--output";output] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () ->
                if not(Path.IsPathFullyQualified output) || Directory.Exists output || File.Exists output then resultExit "backup-integrity-failed" (Error ["backup output must not exist"]) else
                let temporary=output+".tmp-"+Guid.NewGuid().ToString("N")
                try
                    Directory.CreateDirectory temporary |> ignore
                    let mutable failure=None
                    for store in config.Stores do if failure.IsNone then match TelemetryStoreApplication.backupReceiptStore store.Root (assessmentFor store.Root) store.WorkspaceId (Path.Combine(temporary,store.WorkspaceId)) with Ok _->()|Error errors->failure<-Some errors
                    match failure with
                    | Some errors -> resultExit "backup-integrity-failed" (Error errors)
                    | None -> Directory.Move(temporary,output); Console.Out.Write(JsonSerializer.Serialize {|schema="fsgg.telemetry.host-backup-set/1";output=output;workspaces=config.Stores|}+"\n");0
                finally if Directory.Exists temporary then Directory.Delete(temporary,true))
        | ["restore";"--config";path;"--input";input;"--state-root";stateRoot] ->
            match load path with
            | Error errors -> resultExit "invalid-configuration" (Error errors)
            | Ok config -> withLock config (fun () ->
                if not(Path.IsPathFullyQualified stateRoot) || Directory.Exists stateRoot || File.Exists stateRoot then resultExit "backup-integrity-failed" (Error ["restore target must not exist"]) else
                let mutable failure=None
                for store in config.Stores do if failure.IsNone then match TelemetryStoreApplication.restoreReceiptStore (Path.Combine(input,store.WorkspaceId)) (Path.Combine(stateRoot,store.WorkspaceId)) (assessmentFor stateRoot) store.WorkspaceId with Ok _->()|Error errors->failure<-Some errors
                match failure with Some errors->resultExit "backup-integrity-failed" (Error errors)|None->Console.Out.Write(JsonSerializer.Serialize {|schema="fsgg.telemetry.host-restore-set/1";root=stateRoot|}+"\n");0)
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
        | Ok config ->
            try
                match Runtime.ServiceLock.Acquire config.ServiceLockPath with
                | Error _ -> 4
                | Ok serviceLock ->
                  use serviceLock=serviceLock
                  let credentials=Configuration.credentials config
                  match Runtime.recover config with
                  | Error _ -> 3
                  | Ok () ->
                    use state=new Runtime.HostState(config)
                    state.Ready<-true; state.StartDrain()
                    use certificate=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath,File.ReadAllText(config.CertificatePasswordFile).Trim())
                    let builder=Hosting.createBuilder config.ListenUrl certificate
                    let app=builder.Build()
                    Endpoints.configure app state credentials
                    app.Run(); 0
            with :? IOException -> 4
