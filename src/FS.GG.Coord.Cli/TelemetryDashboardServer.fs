namespace FS.GG.Coord.Cli

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Globalization
open System.IO
open System.Net
open System.Net.Sockets
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

type DashboardAsset =
    { ContentType: string
      Content: byte array }

type TelemetryDashboardServerOptions =
    { WorkspaceId: string
      AssetProvider: string -> DashboardAsset option
      SnapshotProvider: string -> CancellationToken -> Task<Result<byte array, string list>>
      BootstrapLifetime: TimeSpan
      SessionIdleTimeout: TimeSpan
      SessionAbsoluteTimeout: TimeSpan
      RequestTimeout: TimeSpan
      SnapshotTimeout: TimeSpan
      ShutdownTimeout: TimeSpan
      MaxSessions: int
      MaxConcurrentRequests: int
      MaxConcurrentQueries: int
      MaxRequestBodyBytes: int
      MaxResponseBodyBytes: int
      MaxHeaderBytes: int
      BindAttempts: int }

type RunningTelemetryDashboardServer =
    inherit IDisposable

    abstract BootstrapUrl: Uri
    abstract Origin: Uri
    abstract Completion: Task
    abstract StopAsync: unit -> Task

module private DashboardServerInternals =
    let utf8 = UTF8Encoding(false)

    let securityHeaders =
        [ "Content-Security-Policy",
          "default-src 'self'; base-uri 'none'; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; object-src 'none'"
          "Referrer-Policy", "no-referrer"
          "X-Content-Type-Options", "nosniff"
          "X-Frame-Options", "DENY"
          "Cache-Control", "private, no-store"
          "Pragma", "no-cache" ]

    let base64Url (bytes: byte array) =
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    let newSecret () =
        RandomNumberGenerator.GetBytes(32) |> base64Url

    let fixedTimeEquals (left: string) (right: string) =
        let a = utf8.GetBytes left
        let b = utf8.GetBytes right
        a.Length = b.Length && CryptographicOperations.FixedTimeEquals(a, b)

    let addSecurityHeaders (response: HttpListenerResponse) =
        for name, value in securityHeaders do
            response.Headers.[name] <- value

    let writeBytes
        (response: HttpListenerResponse)
        status
        contentType
        (content: byte array)
        (cancellationToken: CancellationToken)
        =
        task {
            response.StatusCode <- status
            response.ContentType <- contentType
            response.ContentLength64 <- int64 content.Length
            addSecurityHeaders response

            if content.Length > 0 then
                do! response.OutputStream.WriteAsync(content.AsMemory(), cancellationToken).AsTask()
        }

    let writeEmpty response status cancellationToken =
        writeBytes response status "text/plain; charset=utf-8" Array.empty cancellationToken

    let tryChoosePort () =
        use probe = new TcpListener(IPAddress.Loopback, 0)
        probe.Start()
        let port = (probe.LocalEndpoint :?> IPEndPoint).Port
        probe.Stop()
        port

    let headerSize (request: HttpListenerRequest) =
        request.Headers.AllKeys
        |> Array.sumBy (fun key ->
            if isNull key then
                0
            else
                request.Headers.GetValues(key)
                |> Option.ofObj
                |> Option.defaultValue Array.empty
                |> Array.sumBy (fun value -> key.Length + value.Length + 4))

    let hasAmbiguousHeaders (request: HttpListenerRequest) =
        let duplicateOrCombined =
            [ "Host"
              "Origin"
              "Cookie"
              "Content-Length"
              "Content-Type"
              "Transfer-Encoding" ]
            |> List.exists (fun name ->
                let values =
                    request.Headers.GetValues(name)
                    |> Option.ofObj
                    |> Option.defaultValue Array.empty

                values.Length > 1
                || (not (String.Equals(name, "Cookie", StringComparison.OrdinalIgnoreCase))
                    && values |> Array.exists (fun value -> value.Contains(','))))

        let sessionCookieCount =
            request.Headers.["Cookie"]
            |> Option.ofObj
            |> Option.defaultValue ""
            |> fun value -> value.Split([| ';'; ',' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.filter (fun part ->
                part.TrimStart().StartsWith("fsgg_dashboard_session=", StringComparison.Ordinal))
            |> Array.length

        let lengthAndChunking =
            request.ContentLength64 >= 0L
            && not (String.IsNullOrWhiteSpace request.Headers.["Transfer-Encoding"])

        duplicateOrCombined || sessionCookieCount > 1 || lengthAndChunking

    let hasUnsafeTarget (request: HttpListenerRequest) =
        let target = request.RawUrl

        isNull target
        || not (target.StartsWith("/", StringComparison.Ordinal))
        || target.StartsWith("//", StringComparison.Ordinal)
        || target.Contains('\\')
        || target.Contains('%')
        || target.Contains("..", StringComparison.Ordinal)
        || target.Contains("://", StringComparison.Ordinal)
        || not (String.IsNullOrEmpty request.Url.Query)

    let readBoundedBody limit (request: HttpListenerRequest) (cancellationToken: CancellationToken) =
        task {
            if request.ContentLength64 > int64 limit then
                return Error "request body is too large"
            else
                use buffer = new MemoryStream()
                let chunk = Array.zeroCreate<byte> (min 8192 (limit + 1))
                let mutable total = 0
                let mutable finished = false

                while not finished && total <= limit do
                    let! count =
                        request.InputStream
                            .ReadAsync(chunk.AsMemory(0, min chunk.Length (limit + 1 - total)), cancellationToken)
                            .AsTask()

                    if count = 0 then
                        finished <- true
                    else
                        do! buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken).AsTask()
                        total <- total + count

                if total > limit then
                    return Error "request body is too large"
                else
                    return Ok(buffer.ToArray())
        }

    let tryReadWorkspaceId (bytes: byte array) =
        try
            use document = JsonDocument.Parse(bytes)
            let root = document.RootElement

            if root.ValueKind <> JsonValueKind.Object then
                None
            else
                let properties = root.EnumerateObject() |> Seq.toArray

                if
                    properties.Length = 1
                    && properties.[0].NameEquals("workspaceId")
                    && properties.[0].Value.ValueKind = JsonValueKind.String
                then
                    properties.[0].Value.GetString() |> Option.ofObj
                else
                    None
        with :? JsonException ->
            None

    let isJsonObject (bytes: byte array) =
        try
            use document = JsonDocument.Parse(bytes)
            document.RootElement.ValueKind = JsonValueKind.Object
        with :? JsonException ->
            false

    let validateOptions (options: TelemetryDashboardServerOptions) =
        let errors = ResizeArray<string>()

        let positiveDuration name maximum value =
            if value <= TimeSpan.Zero || value > maximum then
                errors.Add($"{name} must be positive and no greater than {maximum}.")

        let positiveBound name maximum value =
            if value <= 0 || value > maximum then
                errors.Add($"{name} must be between 1 and {maximum}.")

        if
            String.IsNullOrWhiteSpace options.WorkspaceId
            || options.WorkspaceId.Length > 256
        then
            errors.Add("WorkspaceId must contain between 1 and 256 characters.")

        if isNull (box options.AssetProvider) then
            errors.Add("AssetProvider is required.")

        if isNull (box options.SnapshotProvider) then
            errors.Add("SnapshotProvider is required.")

        positiveDuration "BootstrapLifetime" (TimeSpan.FromMinutes 10.0) options.BootstrapLifetime
        positiveDuration "SessionIdleTimeout" (TimeSpan.FromHours 1.0) options.SessionIdleTimeout
        positiveDuration "SessionAbsoluteTimeout" (TimeSpan.FromHours 8.0) options.SessionAbsoluteTimeout
        positiveDuration "RequestTimeout" (TimeSpan.FromMinutes 2.0) options.RequestTimeout
        positiveDuration "SnapshotTimeout" (TimeSpan.FromMinutes 2.0) options.SnapshotTimeout
        positiveDuration "ShutdownTimeout" (TimeSpan.FromSeconds 30.0) options.ShutdownTimeout
        positiveBound "MaxSessions" 1024 options.MaxSessions
        positiveBound "MaxConcurrentRequests" 1024 options.MaxConcurrentRequests
        positiveBound "MaxConcurrentQueries" 256 options.MaxConcurrentQueries
        positiveBound "MaxRequestBodyBytes" (1024 * 1024) options.MaxRequestBodyBytes
        positiveBound "MaxResponseBodyBytes" (16 * 1024 * 1024) options.MaxResponseBodyBytes
        positiveBound "MaxHeaderBytes" (64 * 1024) options.MaxHeaderBytes
        positiveBound "BindAttempts" 32 options.BindAttempts

        if options.SessionIdleTimeout > options.SessionAbsoluteTimeout then
            errors.Add("SessionIdleTimeout cannot exceed SessionAbsoluteTimeout.")

        List.ofSeq errors

    type Session(createdAt: int64) =
        let gate = obj ()
        let mutable lastSeenAt = createdAt

        member _.IsExpired(now, idleTimeout, absoluteTimeout) =
            lock gate (fun () ->
                Stopwatch.GetElapsedTime(lastSeenAt, now) > idleTimeout
                || Stopwatch.GetElapsedTime(createdAt, now) > absoluteTimeout)

        member _.TryTouch(now, idleTimeout, absoluteTimeout) =
            lock gate (fun () ->
                if
                    Stopwatch.GetElapsedTime(lastSeenAt, now) > idleTimeout
                    || Stopwatch.GetElapsedTime(createdAt, now) > absoluteTimeout
                then
                    false
                else
                    lastSeenAt <- now
                    true)

    type ServerState
        (options: TelemetryDashboardServerOptions, listener: HttpListener, port: int, bootstrapToken: string) =
        let originText = $"http://127.0.0.1:{port}"
        let origin = Uri(originText)
        let bootstrapUrl = Uri($"{originText}/bootstrap/{bootstrapToken}")
        let bootstrapStartedAt = Stopwatch.GetTimestamp()
        let sessions = ConcurrentDictionary<string, Session>(StringComparer.Ordinal)

        let requests =
            new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests)

        let queries =
            new SemaphoreSlim(options.MaxConcurrentQueries, options.MaxConcurrentQueries)

        let shutdown = new CancellationTokenSource()
        let mutable bootstrapSecret = bootstrapToken
        let mutable bootstrapAvailable = 1
        let mutable stopStarted = 0
        let mutable completion: Task = Task.CompletedTask
        let registrationGate = obj ()
        let mutable externalRegistration: CancellationTokenRegistration option = None

        let closeResponse (response: HttpListenerResponse) =
            try
                response.Close()
            with _ ->
                ()

        let sessionCookie (token: string) (maxAge: int) =
            $"fsgg_dashboard_session={token}; Path=/; HttpOnly; SameSite=Strict; Max-Age={maxAge.ToString(CultureInfo.InvariantCulture)}"

        let expireSessions now =
            for KeyValue(token, session) in sessions do
                if session.IsExpired(now, options.SessionIdleTimeout, options.SessionAbsoluteTimeout) then
                    sessions.TryRemove token |> ignore

        let tryAuthenticate (request: HttpListenerRequest) =
            let now = Stopwatch.GetTimestamp()
            expireSessions now
            let cookie = request.Cookies.["fsgg_dashboard_session"]

            if isNull cookie || String.IsNullOrEmpty cookie.Value then
                false
            else
                match sessions.TryGetValue cookie.Value with
                | true, session ->
                    if not (session.TryTouch(now, options.SessionIdleTimeout, options.SessionAbsoluteTimeout)) then
                        sessions.TryRemove cookie.Value |> ignore
                        false
                    else
                        true
                | _ -> false

        let exactOrigin (request: HttpListenerRequest) =
            String.Equals(request.Headers.["Origin"], originText, StringComparison.Ordinal)

        let abortResponse (response: HttpListenerResponse) =
            try
                response.Abort()
            with _ ->
                ()

        let routeBootstrap (request: HttpListenerRequest) (response: HttpListenerResponse) token cancellationToken =
            task {
                let now = Stopwatch.GetTimestamp()
                expireSessions now

                if
                    Stopwatch.GetElapsedTime(bootstrapStartedAt, now) > options.BootstrapLifetime
                    || Volatile.Read(&bootstrapAvailable) <> 1
                    || not (fixedTimeEquals token bootstrapSecret)
                then
                    do! writeEmpty response 404 cancellationToken
                elif sessions.Count >= options.MaxSessions then
                    do! writeEmpty response 503 cancellationToken
                elif Interlocked.CompareExchange(&bootstrapAvailable, 0, 1) <> 1 then
                    do! writeEmpty response 404 cancellationToken
                else
                    bootstrapSecret <- newSecret ()
                    let sessionToken = newSecret ()
                    sessions.[sessionToken] <- Session(now)
                    let maxAge = options.SessionAbsoluteTimeout.TotalSeconds |> Math.Ceiling |> int
                    response.Headers.["Set-Cookie"] <- sessionCookie sessionToken maxAge
                    response.RedirectLocation <- "/"
                    do! writeEmpty response 303 cancellationToken
            }

        let routeAsset (response: HttpListenerResponse) path cancellationToken =
            task {
                match options.AssetProvider path with
                | Some asset when
                    not (isNull asset.Content)
                    && asset.Content.Length <= options.MaxResponseBodyBytes
                    && not (String.IsNullOrWhiteSpace asset.ContentType)
                    ->
                    do! writeBytes response 200 asset.ContentType asset.Content cancellationToken
                | _ -> do! writeEmpty response 404 cancellationToken
            }

        let routeSnapshot
            (request: HttpListenerRequest)
            (response: HttpListenerResponse)
            (cancellationToken: CancellationToken)
            =
            task {
                let mediaType =
                    request.ContentType
                    |> Option.ofObj
                    |> Option.map (fun value -> value.Split(';').[0].Trim())

                if
                    not (
                        mediaType
                        |> Option.exists (fun value ->
                            String.Equals(value, "application/json", StringComparison.OrdinalIgnoreCase))
                    )
                then
                    do! writeEmpty response 415 cancellationToken
                else
                    let! body = readBoundedBody options.MaxRequestBodyBytes request cancellationToken

                    match body with
                    | Error _ -> do! writeEmpty response 413 cancellationToken
                    | Ok bytes ->
                        match tryReadWorkspaceId bytes with
                        | Some workspaceId when fixedTimeEquals workspaceId options.WorkspaceId ->
                            if not (queries.Wait(0)) then
                                do! writeEmpty response 429 cancellationToken
                            else
                                let providerWork: Task<Result<byte array, string list>> =
                                    Task.Factory
                                        .StartNew(
                                            Func<Task<Result<byte array, string list>>>(fun () ->
                                                task {
                                                    try
                                                        let providerTask =
                                                            options.SnapshotProvider options.WorkspaceId shutdown.Token

                                                        if isNull providerTask then
                                                            return Error [ "Snapshot provider returned no task." ]
                                                        else
                                                            return! providerTask
                                                    with _ ->
                                                        return Error [ "Snapshot provider failed." ]
                                                }),
                                            CancellationToken.None,
                                            TaskCreationOptions.DenyChildAttach,
                                            TaskScheduler.Default
                                        )
                                        .Unwrap()

                                providerWork.ContinueWith(
                                    (fun (_: Task<Result<byte array, string list>>) -> queries.Release() |> ignore),
                                    CancellationToken.None,
                                    TaskContinuationOptions.ExecuteSynchronously,
                                    TaskScheduler.Default
                                )
                                |> ignore

                                try
                                    let! result = providerWork.WaitAsync(options.SnapshotTimeout, cancellationToken)

                                    match result with
                                    | Ok snapshot when
                                        not (isNull snapshot) && snapshot.Length <= options.MaxResponseBodyBytes
                                        ->
                                        if isJsonObject snapshot then
                                            do!
                                                writeBytes
                                                    response
                                                    200
                                                    "application/json; charset=utf-8"
                                                    snapshot
                                                    cancellationToken
                                        else
                                            do! writeEmpty response 503 cancellationToken
                                    | _ -> do! writeEmpty response 503 cancellationToken
                                with
                                | :? TimeoutException -> do! writeEmpty response 504 cancellationToken
                                | :? OperationCanceledException when cancellationToken.IsCancellationRequested -> ()
                                | _ -> do! writeEmpty response 503 cancellationToken
                        | _ -> do! writeEmpty response 403 cancellationToken
            }

        let routeLogout (request: HttpListenerRequest) (response: HttpListenerResponse) cancellationToken =
            task {
                let cookie = request.Cookies.["fsgg_dashboard_session"]

                if not (isNull cookie) then
                    sessions.TryRemove cookie.Value |> ignore

                response.Headers.["Set-Cookie"] <- sessionCookie "expired" 0
                do! writeEmpty response 204 cancellationToken
            }

        let routeSession (response: HttpListenerResponse) cancellationToken =
            task {
                let bytes =
                    JsonSerializer.SerializeToUtf8Bytes(
                        {| schema = "fsgg.telemetry.browser-session/1"
                           workspaces = [| options.WorkspaceId |] |}
                    )

                do! writeBytes response 200 "application/json; charset=utf-8" bytes cancellationToken
            }

        let processContext (context: HttpListenerContext) (requestLifetime: CancellationTokenSource) =
            task {
                use requestLifetime = requestLifetime
                let cancellationToken = requestLifetime.Token
                let response = context.Response
                response.StatusCode <- 503

                use timeoutAbort =
                    cancellationToken.Register(fun () -> abortResponse response)

                try
                    try
                        let request = context.Request
                        let remote = request.RemoteEndPoint
                        let expectedHost = $"127.0.0.1:{port}"

                        if isNull remote || not (IPAddress.IsLoopback remote.Address) then
                            do! writeEmpty response 403 cancellationToken
                        elif headerSize request > options.MaxHeaderBytes || hasAmbiguousHeaders request then
                            do! writeEmpty response 400 cancellationToken
                        elif not (String.Equals(request.Headers.["Host"], expectedHost, StringComparison.Ordinal)) then
                            do! writeEmpty response 400 cancellationToken
                        elif hasUnsafeTarget request then
                            do! writeEmpty response 400 cancellationToken
                        else
                            let path = request.Url.AbsolutePath

                            match request.HttpMethod, path with
                            | "GET", value when value.StartsWith("/bootstrap/", StringComparison.Ordinal) ->
                                let token = value.Substring("/bootstrap/".Length)

                                if request.HasEntityBody then
                                    do! writeEmpty response 400 cancellationToken
                                elif token.Length = 0 || token.Contains('/') then
                                    do! writeEmpty response 404 cancellationToken
                                else
                                    do! routeBootstrap request response token cancellationToken
                            | "GET", ("/" | "/index.html" | "/app.js" | "/styles.css") ->
                                if request.HasEntityBody then
                                    do! writeEmpty response 400 cancellationToken
                                elif tryAuthenticate request then
                                    do! routeAsset response path cancellationToken
                                else
                                    do! writeEmpty response 401 cancellationToken
                            | "POST", "/api/snapshot" ->
                                if not (tryAuthenticate request) then
                                    do! writeEmpty response 401 cancellationToken
                                elif not (exactOrigin request) then
                                    do! writeEmpty response 403 cancellationToken
                                else
                                    do! routeSnapshot request response cancellationToken
                            | "POST", "/api/session" ->
                                if not (tryAuthenticate request) then
                                    do! writeEmpty response 401 cancellationToken
                                elif not (exactOrigin request) then
                                    do! writeEmpty response 403 cancellationToken
                                elif request.HasEntityBody then
                                    do! writeEmpty response 400 cancellationToken
                                else
                                    do! routeSession response cancellationToken
                            | "POST", "/api/logout" ->
                                if not (tryAuthenticate request) then
                                    do! writeEmpty response 401 cancellationToken
                                elif not (exactOrigin request) then
                                    do! writeEmpty response 403 cancellationToken
                                elif request.HasEntityBody then
                                    do! writeEmpty response 400 cancellationToken
                                else
                                    do! routeLogout request response cancellationToken
                            | ("GET" | "POST"), _ -> do! writeEmpty response 404 cancellationToken
                            | _ -> do! writeEmpty response 405 cancellationToken
                    with
                    | :? OperationCanceledException when cancellationToken.IsCancellationRequested -> ()
                    | :? HttpListenerException -> ()
                    | :? IOException -> ()
                    | :? ObjectDisposedException -> ()
                    | _ ->
                        try
                            do! writeEmpty response 500 cancellationToken
                        with _ ->
                            ()
                finally
                    if cancellationToken.IsCancellationRequested then
                        abortResponse response
                    else
                        closeResponse response
            }

        let acceptLoop () =
            task {
                try
                    while not shutdown.IsCancellationRequested && listener.IsListening do
                        let! context = listener.GetContextAsync().WaitAsync(shutdown.Token)

                        if requests.Wait(0) then
                            let requestLifetime =
                                CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token)

                            requestLifetime.CancelAfter(options.RequestTimeout)

                            let handling =
                                Task.Run(Func<Task>(fun () -> processContext context requestLifetime))

                            handling.ContinueWith(
                                (fun (_: Task) -> requests.Release() |> ignore),
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default
                            )
                            |> ignore
                        else
                            try
                                context.Response.StatusCode <- 503
                                context.Response.ContentLength64 <- 0L
                                addSecurityHeaders context.Response
                            finally
                                closeResponse context.Response
                with
                | :? OperationCanceledException -> ()
                | :? HttpListenerException when shutdown.IsCancellationRequested -> ()
                | :? ObjectDisposedException when shutdown.IsCancellationRequested -> ()
            }

        let stop () =
            task {
                if Interlocked.CompareExchange(&stopStarted, 1, 0) = 0 then
                    let registration =
                        lock registrationGate (fun () ->
                            let current = externalRegistration
                            externalRegistration <- None
                            current)

                    match registration with
                    | Some value -> value.Dispose()
                    | None -> ()

                    sessions.Clear()
                    shutdown.Cancel()

                    try
                        listener.Close()
                    with _ ->
                        ()

                try
                    do! completion.WaitAsync(options.ShutdownTimeout)
                with
                | :? TimeoutException -> ()
                | :? OperationCanceledException -> ()
                | _ -> ()
            }

        member _.Start() = completion <- acceptLoop ()

        member this.AttachCancellation(cancellationToken: CancellationToken) =
            if cancellationToken.CanBeCanceled then
                let registration =
                    cancellationToken.Register(fun () ->
                        ThreadPool.QueueUserWorkItem(WaitCallback(fun _ -> this.StopAsync() |> ignore))
                        |> ignore)

                let disposeImmediately =
                    lock registrationGate (fun () ->
                        if Volatile.Read(&stopStarted) <> 0 then
                            true
                        else
                            externalRegistration <- Some registration
                            false)

                if disposeImmediately then
                    registration.Dispose()

        member _.BootstrapUrl = bootstrapUrl
        member _.Origin = origin
        member _.Completion = completion
        member _.StopAsync() = stop ()

        interface RunningTelemetryDashboardServer with
            member this.BootstrapUrl = this.BootstrapUrl
            member this.Origin = this.Origin
            member this.Completion = this.Completion
            member this.StopAsync() = this.StopAsync()

            member this.Dispose() =
                this.StopAsync().GetAwaiter().GetResult()

[<RequireQualifiedAccess>]
module TelemetryDashboardServer =
    open DashboardServerInternals

    let defaultOptions
        (workspaceId: string)
        (assetProvider: string -> DashboardAsset option)
        (snapshotProvider: string -> CancellationToken -> Task<Result<byte array, string list>>)
        =
        { WorkspaceId = workspaceId
          AssetProvider = assetProvider
          SnapshotProvider = snapshotProvider
          BootstrapLifetime = TimeSpan.FromMinutes 2.0
          SessionIdleTimeout = TimeSpan.FromMinutes 10.0
          SessionAbsoluteTimeout = TimeSpan.FromMinutes 30.0
          RequestTimeout = TimeSpan.FromSeconds 15.0
          SnapshotTimeout = TimeSpan.FromSeconds 10.0
          ShutdownTimeout = TimeSpan.FromSeconds 5.0
          MaxSessions = 8
          MaxConcurrentRequests = 16
          MaxConcurrentQueries = 2
          MaxRequestBodyBytes = 4096
          MaxResponseBodyBytes = 4 * 1024 * 1024
          MaxHeaderBytes = 16384
          BindAttempts = 8 }

    let start (options: TelemetryDashboardServerOptions) (cancellationToken: CancellationToken) =
        task {
            let errors = validateOptions options

            if not errors.IsEmpty then
                return Error errors
            elif cancellationToken.IsCancellationRequested then
                return Error [ "Dashboard server startup was cancelled." ]
            else
                let mutable attempt = 0
                let mutable started: (HttpListener * int) option = None
                let failures = ResizeArray<string>()

                while attempt < options.BindAttempts
                      && started.IsNone
                      && not cancellationToken.IsCancellationRequested do
                    attempt <- attempt + 1
                    let listener = new HttpListener()

                    try
                        let port = tryChoosePort ()
                        listener.Prefixes.Add($"http://127.0.0.1:{port}/")
                        listener.Start()
                        started <- Some(listener, port)
                    with
                    | :? HttpListenerException as error ->
                        failures.Add($"Loopback bind attempt {attempt} failed: {error.ErrorCode}.")
                        listener.Close()
                    | _ ->
                        failures.Add($"Loopback bind attempt {attempt} failed.")
                        listener.Close()

                match started with
                | None when cancellationToken.IsCancellationRequested ->
                    return Error [ "Dashboard server startup was cancelled." ]
                | None -> return Error(List.ofSeq failures)
                | Some(listener, port) ->
                    if cancellationToken.IsCancellationRequested then
                        listener.Close()
                        return Error [ "Dashboard server startup was cancelled." ]
                    else
                        let state = new ServerState(options, listener, port, newSecret ())
                        state.Start()

                        state.AttachCancellation(cancellationToken)

                        return Ok(state :> RunningTelemetryDashboardServer)
        }
