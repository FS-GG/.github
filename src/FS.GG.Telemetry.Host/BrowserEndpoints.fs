namespace FS.GG.Telemetry.Host

open System
open System.IO
open System.IO.Compression
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord
open FS.GG.Telemetry.Dashboard
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Net.Http.Headers

type BrowserSnapshotProvider = string -> string option -> Result<byte array,string list>

module BrowserEndpoints =
    [<Literal>]
    let private CookieName="__Host-fsgg_session"
    [<Literal>]
    let private MaxRequestBytes=1024
    [<Literal>]
    let private MaxResponseBytes=524288

    let private headers (context:HttpContext) privateResponse =
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] <- "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'"
        context.Response.Headers[HeaderNames.XContentTypeOptions] <- "nosniff"
        context.Response.Headers["Referrer-Policy"] <- "no-referrer"
        context.Response.Headers[HeaderNames.XFrameOptions] <- "DENY"
        if privateResponse then context.Response.Headers[HeaderNames.CacheControl] <- "no-store"
        else context.Response.Headers[HeaderNames.CacheControl] <- "no-cache"
    let private json context status code = task {
        headers context true
        context.Response.StatusCode<-status
        context.Response.ContentType<-"application/json"
        let bytes=JsonSerializer.SerializeToUtf8Bytes {|schema="fsgg.telemetry.browser-error/1";code=code|}
        do! context.Response.Body.WriteAsync bytes }
    let private hostMatches (options:BrowserOptions) (context:HttpContext) =
        context.Request.IsHttps && String.Equals(context.Request.Host.Value,options.PublicOrigin.Authority,StringComparison.OrdinalIgnoreCase)
    let private originMatches (options:BrowserOptions) (context:HttpContext) =
        match context.Request.Headers.TryGetValue HeaderNames.Origin with
        | true,values when values.Count=1 -> String.Equals(string values[0],options.PublicOrigin.GetLeftPart(UriPartial.Authority),StringComparison.Ordinal)
        | _ -> false
    let private jsonContent (context:HttpContext) =
        try
            match context.Request.GetTypedHeaders().ContentType with
            | null -> false
            | value -> String.Equals(value.MediaType.Value,"application/json",StringComparison.OrdinalIgnoreCase)
        with :? FormatException -> false
    let private readBounded (context:HttpContext) (token:CancellationToken) = task {
        if context.Request.ContentLength.HasValue && context.Request.ContentLength.Value>int64 MaxRequestBytes then return None else
        use output=new MemoryStream()
        let buffer=Array.zeroCreate<byte> 512
        let mutable total=0
        let mutable reading=true
        while reading && total<=MaxRequestBytes do
            let! count=context.Request.Body.ReadAsync(buffer.AsMemory(0,buffer.Length),token)
            if count=0 then reading<-false else output.Write(buffer,0,count);total<-total+count
        return if total>MaxRequestBytes then None else Some(output.ToArray()) }
    let private cookie (context:HttpContext) =
        match context.Request.Cookies.TryGetValue CookieName with true,value when value.Length=43->Some value | _->None
    let private setCookie (options:BrowserOptions) (context:HttpContext) (value:string) =
        let cookieOptions=CookieOptions()
        cookieOptions.HttpOnly<-true
        cookieOptions.Secure<-true
        cookieOptions.SameSite<-Microsoft.AspNetCore.Http.SameSiteMode.Strict
        cookieOptions.Path<-"/"
        cookieOptions.IsEssential<-true
        cookieOptions.MaxAge<-Nullable options.IdleLifetime
        context.Response.Cookies.Append(CookieName,value,cookieOptions)
    let private sessionResponse (context:HttpContext) (identity:BrowserIdentity) = task {
        headers context true
        context.Response.StatusCode<-200
        context.Response.ContentType<-"application/json"
        let bytes=JsonSerializer.SerializeToUtf8Bytes {|schema="fsgg.telemetry.browser-session/1";workspaces=identity.WorkspaceIds|>Set.toArray|}
        do! context.Response.Body.WriteAsync bytes }
    let private expireCookie (context:HttpContext) =
        let cookieOptions=CookieOptions()
        cookieOptions.HttpOnly<-true
        cookieOptions.Secure<-true
        cookieOptions.SameSite<-Microsoft.AspNetCore.Http.SameSiteMode.Strict
        cookieOptions.Path<-"/"
        cookieOptions.IsEssential<-true
        context.Response.Cookies.Delete(CookieName,cookieOptions)
    let private identity (security:BrowserSecurity.Service) context = cookie context |> Option.bind(fun value->security.Validate(value,DateTimeOffset.UtcNow))
    let private exactObject (element:JsonElement) (names:string array) =
        if element.ValueKind<>JsonValueKind.Object then false else
        let actual=element.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
        actual.Length=names.Length && actual|>Array.distinct|>Array.length=actual.Length && Set.ofArray actual=Set.ofArray names
    let private scalar (element:JsonElement) (name:string) =
        match element.TryGetProperty name with true,value when value.ValueKind=JsonValueKind.String->Some(value.GetString()) | _->None
    let private validOptionalId = function None->true | Some value->TelemetryReceipt.validId value
    let private gzip (bytes:byte array) =
        use output=new MemoryStream()
        use compressor=new GZipStream(output,CompressionLevel.Fastest,true)
        compressor.Write(bytes,0,bytes.Length);compressor.Close();output.ToArray()
    let private acceptsGzip (context:HttpContext) =
        context.Request.GetTypedHeaders().AcceptEncoding
        |> Seq.exists(fun value->String.Equals(value.Value.ToString(),"gzip",StringComparison.OrdinalIgnoreCase) && (not value.Quality.HasValue || value.Quality.Value>0.))

    let map (endpoints:IEndpointRouteBuilder) (options:BrowserOptions) (security:BrowserSecurity.Service) (snapshot:BrowserSnapshotProvider) =
        let options=BrowserSecurity.validateOptions options |> Result.defaultWith(fun errors->invalidArg "options" (String.concat ";" errors))
        let staticAsset (route:string) = Func<HttpContext,Task>(fun context -> task {
            if not(hostMatches options context) then do! json context 404 "not-found" else
            match DashboardAssets.tryGet route with
            | None -> do! json context 404 "not-found"
            | Some asset ->
                headers context false
                context.Response.StatusCode<-200
                context.Response.ContentType<-asset.ContentType
                do! context.Response.Body.WriteAsync asset.Bytes })
        endpoints.MapGet("/private/dashboard/",staticAsset "/private/dashboard/") |> ignore
        endpoints.MapGet("/private/dashboard/app.js",staticAsset "/private/dashboard/app.js") |> ignore
        endpoints.MapGet("/private/dashboard/styles.css",staticAsset "/private/dashboard/styles.css") |> ignore
        endpoints.MapPost("/private/dashboard/login",Func<HttpContext,Task>(fun context -> task {
            if not(hostMatches options context && originMatches options context && jsonContent context) then do! json context 400 "invalid-request" else
            if not(security.TryAcquireLogin()) then do! json context 429 "overload" else
            try
              try
                use deadline=CancellationTokenSource.CreateLinkedTokenSource context.RequestAborted
                deadline.CancelAfter(TimeSpan.FromSeconds 10.)
                match! readBounded context deadline.Token with
                | None -> do! json context 413 "request-too-large"
                | Some bytes ->
                  try
                      use document=JsonDocument.Parse(bytes,JsonDocumentOptions(MaxDepth=3))
                      let root=document.RootElement
                      match exactObject root [|"principalId";"accessKey"|],scalar root "principalId",scalar root "accessKey" with
                      | true,Some principal,Some key ->
                          match security.LoginAcquired(principal,key,DateTimeOffset.UtcNow) with
                          | LoginAccepted(session,identity) -> setCookie options context session;do! sessionResponse context identity
                          | LoginDenied -> do! json context 401 "authentication-failed"
                          | LoginOverloaded -> do! json context 429 "overload"
                      | _ -> do! json context 400 "invalid-request"
                  with _ -> do! json context 400 "invalid-request"
              with :? OperationCanceledException -> if not context.Response.HasStarted then do! json context 408 "request-timeout"
            finally security.ReleaseLogin() })) |> ignore
        endpoints.MapPost("/private/dashboard/v1/logout",Func<HttpContext,Task>(fun context -> task {
            if not(hostMatches options context && originMatches options context) then do! json context 400 "invalid-request" else
            cookie context |> Option.iter security.Logout
            expireCookie context;headers context true;context.Response.StatusCode<-204 })) |> ignore
        endpoints.MapPost("/private/dashboard/v1/session/refresh",Func<HttpContext,Task>(fun context -> task {
            if not(hostMatches options context && originMatches options context) then do! json context 400 "invalid-request" else
            match cookie context |> Option.bind(fun value->security.Rotate(value,DateTimeOffset.UtcNow)) with
            | None -> do! json context 401 "authentication-required"
            | Some(replacement,identity) -> setCookie options context replacement;do! sessionResponse context identity })) |> ignore
        endpoints.MapPost("/private/dashboard/v1/snapshot",Func<HttpContext,Task>(fun context -> task {
            if not(hostMatches options context && originMatches options context && jsonContent context) then do! json context 400 "invalid-request" else
            match identity security context with
            | None -> do! json context 401 "authentication-required"
            | Some browser ->
                if not(security.TryAcquireQuery()) then do! json context 429 "overload" else
                let mutable transferred=false
                try
                  try
                    use deadline=CancellationTokenSource.CreateLinkedTokenSource context.RequestAborted
                    deadline.CancelAfter(options.QueryTimeout)
                    match! readBounded context deadline.Token with
                    | None -> do! json context 413 "request-too-large"
                    | Some bytes ->
                      try
                        use document=JsonDocument.Parse(bytes,JsonDocumentOptions(MaxDepth=3))
                        let root=document.RootElement
                        let names=root.EnumerateObject() |> Seq.map _.Name |> Seq.toArray
                        let shape=(names.Length=1 || names.Length=2) && names|>Array.distinct|>Array.length=names.Length && Set.isSubset (Set.ofArray names) (set["workspaceId";"itemId"])
                        let workspace=scalar root "workspaceId"
                        let item=scalar root "itemId"
                        if not shape || workspace.IsNone || not(TelemetryReceipt.validId workspace.Value) || not(validOptionalId item) then do! json context 400 "invalid-request"
                        elif not(browser.WorkspaceIds.Contains workspace.Value) then do! json context 404 "workspace-unavailable"
                        else
                            transferred<-true
                            let work:Task<Result<byte array,string list>>=Task.Run(fun()->snapshot workspace.Value item)
                            work.ContinueWith(fun (_:Task<Result<byte array,string list>>)->security.ReleaseQuery()) |> ignore
                            let workTask:Task=work :> Task
                            let! completed=Task.WhenAny(workTask,Task.Delay options.QueryTimeout)
                            if not(Object.ReferenceEquals(completed,workTask)) then do! json context 503 "query-timeout" else
                            let outcome =
                                if work.IsFaulted || work.IsCanceled then None
                                else Some(work.GetAwaiter().GetResult())
                            match outcome with
                            | None -> do! json context 503 "query-unavailable"
                            | Some(Error _) -> do! json context 404 "workspace-unavailable"
                            | Some(Ok response) when response.Length>MaxResponseBytes -> do! json context 503 "response-too-large"
                            | Some(Ok response) ->
                                let compressed=acceptsGzip context
                                let body=if compressed then gzip response else response
                                if body.Length>MaxResponseBytes then do! json context 503 "response-too-large" else
                                headers context true
                                context.Response.StatusCode<-200
                                context.Response.ContentType<-"application/json"
                                context.Response.Headers.ETag <- Microsoft.Extensions.Primitives.StringValues("\""+Convert.ToHexString(SHA256.HashData response).ToLowerInvariant()+"\"")
                                if compressed then context.Response.Headers.ContentEncoding<-"gzip"
                                do! context.Response.Body.WriteAsync body
                      with _ -> do! json context 400 "invalid-request"
                  with :? OperationCanceledException -> if not context.Response.HasStarted then do! json context 408 "request-timeout"
                finally if not transferred then security.ReleaseQuery() })) |> ignore
