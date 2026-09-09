namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

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

module Program =
    [<EntryPoint>]
    let main argv =
        if argv.Length<>1 then 2 else
        match Configuration.load argv[0] with
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
