namespace FS.GG.Telemetry.LocalDashboard.Tests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open FS.GG.Coord.Cli
open Xunit

module ServerTests =
    let private workspaceId = "workspace-alpha"

    let private asset (contentType: string) (content: string) =
        { ContentType = contentType
          Content = Encoding.UTF8.GetBytes content }

    let private baseOptions snapshotProvider =
        TelemetryDashboardServer.defaultOptions
            workspaceId
            (function
            | "/"
            | "/index.html" ->
                Some(
                    asset
                        "text/html; charset=utf-8"
                        "<!doctype html><title>Private dashboard</title><main id=workspace></main><script src=/app.js></script>"
                )
            | "/app.js" ->
                Some(
                    asset
                        "text/javascript; charset=utf-8"
                        "fetch('/api/snapshot',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({workspaceId:'workspace-alpha'})}).then(r=>r.json()).then(x=>document.querySelector('#workspace').textContent=x.workspaceId)"
                )
            | "/styles.css" -> Some(asset "text/css; charset=utf-8" "body{font-family:sans-serif}")
            | _ -> None)
            snapshotProvider

    let private immediateOptions () =
        baseOptions (fun selectedWorkspace _ ->
            Task.FromResult(Ok(Encoding.UTF8.GetBytes($"{{\"workspaceId\":\"{selectedWorkspace}\"}}"))))

    let private start options =
        task {
            let! result = TelemetryDashboardServer.start options CancellationToken.None

            return
                match result with
                | Ok server -> server
                | Error errors -> failwith (String.concat "; " errors)
        }

    let private client () =
        let handler =
            new HttpClientHandler(AllowAutoRedirect = false, UseCookies = true, CookieContainer = CookieContainer())

        new HttpClient(handler), handler

    let private bootstrap (http: HttpClient) (server: RunningTelemetryDashboardServer) =
        task {
            use! response = http.GetAsync(server.BootstrapUrl)
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode)
            return response.Headers.GetValues("Set-Cookie") |> Seq.exactlyOne
        }

    let private snapshotRequest
        (server: RunningTelemetryDashboardServer)
        (selectedWorkspace: string)
        (origin: string option)
        =
        let request =
            new HttpRequestMessage(HttpMethod.Post, Uri(server.Origin, "/api/snapshot"))

        match origin with
        | Some value -> request.Headers.Add("Origin", value)
        | None -> ()

        request.Content <-
            new StringContent($"{{\"workspaceId\":\"{selectedWorkspace}\"}}", Encoding.UTF8, "application/json")

        request

    let private rawRequest (server: RunningTelemetryDashboardServer) (requestText: string) =
        task {
            use tcp = new TcpClient()
            do! tcp.ConnectAsync(IPAddress.Loopback, server.Origin.Port)
            use stream = tcp.GetStream()
            let bytes = Encoding.ASCII.GetBytes requestText
            do! stream.WriteAsync(bytes.AsMemory()).AsTask()
            do! stream.FlushAsync()
            use reader = new StreamReader(stream, Encoding.ASCII)
            return! reader.ReadToEndAsync()
        }

    [<Fact>]
    let ``bootstrap exchanges once and authenticated snapshot is workspace scoped`` () =
        task {
            use! server = start (immediateOptions ())
            let http, handler = client ()
            use http = http
            use handler = handler

            let! setCookie = bootstrap http server
            Assert.Contains("HttpOnly", setCookie)
            Assert.Contains("SameSite=Strict", setCookie)
            Assert.DoesNotContain("workspace-alpha", server.BootstrapUrl.AbsoluteUri)

            use! replay = http.GetAsync(server.BootstrapUrl)
            Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode)

            use request =
                snapshotRequest server workspaceId (Some(server.Origin.GetLeftPart(UriPartial.Authority)))

            use! response = http.SendAsync(request)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.True(response.Headers.CacheControl.NoStore)
            Assert.True(response.Headers.CacheControl.Private)
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options") |> Seq.exactlyOne)
            let! body = response.Content.ReadAsStringAsync()
            Assert.Equal("{\"workspaceId\":\"workspace-alpha\"}", body)

            let cookies = handler.CookieContainer.GetCookies(server.Origin)
            Assert.NotEmpty(cookies)
        }

    [<Fact>]
    let ``host origin authentication and workspace scope are exact`` () =
        task {
            use! server = start (immediateOptions ())
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server

            use wrongHost = new HttpRequestMessage(HttpMethod.Get, Uri(server.Origin, "/"))
            wrongHost.Headers.Host <- $"localhost:{server.Origin.Port}"
            use! wrongHostResponse = http.SendAsync(wrongHost)
            Assert.NotEqual(HttpStatusCode.OK, wrongHostResponse.StatusCode)

            use noOrigin = snapshotRequest server workspaceId None
            use! noOriginResponse = http.SendAsync(noOrigin)
            Assert.Equal(HttpStatusCode.Forbidden, noOriginResponse.StatusCode)

            use wrongOrigin =
                snapshotRequest server workspaceId (Some "http://attacker.invalid")

            use! wrongOriginResponse = http.SendAsync(wrongOrigin)
            Assert.Equal(HttpStatusCode.Forbidden, wrongOriginResponse.StatusCode)

            use wrongWorkspace =
                snapshotRequest server "workspace-beta" (Some(server.Origin.GetLeftPart(UriPartial.Authority)))

            use! wrongWorkspaceResponse = http.SendAsync(wrongWorkspace)
            Assert.Equal(HttpStatusCode.Forbidden, wrongWorkspaceResponse.StatusCode)

            let anonymous, anonymousHandler = client ()
            use anonymous = anonymous
            use anonymousHandler = anonymousHandler

            use anonymousRequest =
                snapshotRequest server workspaceId (Some(server.Origin.GetLeftPart(UriPartial.Authority)))

            use! anonymousResponse = anonymous.SendAsync(anonymousRequest)
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode)
        }

    [<Fact>]
    let ``fixed routes reject methods bodies and oversized input`` () =
        task {
            let options =
                { immediateOptions () with
                    MaxRequestBodyBytes = 32 }

            use! server = start options
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server

            use missing =
                new HttpRequestMessage(HttpMethod.Get, Uri(server.Origin, "/favicon.ico"))

            use! missingResponse = http.SendAsync(missing)
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode)

            use put =
                new HttpRequestMessage(HttpMethod.Put, Uri(server.Origin, "/api/snapshot"))

            use! putResponse = http.SendAsync(put)
            Assert.Equal(HttpStatusCode.MethodNotAllowed, putResponse.StatusCode)

            use getWithBody = new HttpRequestMessage(HttpMethod.Get, Uri(server.Origin, "/"))
            getWithBody.Content <- new StringContent("unexpected")
            use! getWithBodyResponse = http.SendAsync(getWithBody)
            Assert.Equal(HttpStatusCode.BadRequest, getWithBodyResponse.StatusCode)

            use oversized =
                snapshotRequest server (String('x', 80)) (Some(server.Origin.GetLeftPart(UriPartial.Authority)))

            use! oversizedResponse = http.SendAsync(oversized)
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode)

            use wrongType =
                new HttpRequestMessage(HttpMethod.Post, Uri(server.Origin, "/api/snapshot"))

            wrongType.Headers.Add("Origin", server.Origin.GetLeftPart(UriPartial.Authority))
            wrongType.Content <- new StringContent("workspace-alpha", Encoding.UTF8, "text/plain")
            use! wrongTypeResponse = http.SendAsync(wrongType)
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongTypeResponse.StatusCode)
        }

    [<Fact>]
    let ``raw targets traversal and ambiguous origin are refused`` () =
        task {
            use! server = start (immediateOptions ())
            let host = $"127.0.0.1:{server.Origin.Port}"
            let http, handler = client ()
            use http = http
            use handler = handler
            let! setCookie = bootstrap http server
            let sessionCookie = setCookie.Split(';').[0]

            let! absolute =
                rawRequest server $"GET http://{host}/ HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n"

            Assert.Contains(" 400 ", absolute.Split('\n').[0])

            let! forgedHost =
                rawRequest server $"GET / HTTP/1.1\r\nHost: localhost:{server.Origin.Port}\r\nConnection: close\r\n\r\n"

            Assert.DoesNotContain(" 200 ", forgedHost.Split('\n').[0])

            let! encoded =
                rawRequest
                    server
                    ("GET /%2e%2e/index.html HTTP/1.1\r\nHost: "
                     + host
                     + "\r\nConnection: close\r\n\r\n")

            Assert.Contains(" 400 ", encoded.Split('\n').[0])

            let! duplicateOrigin =
                rawRequest
                    server
                    $"POST /api/snapshot HTTP/1.1\r\nHost: {host}\r\nOrigin: http://{host}\r\nOrigin: http://attacker.invalid\r\nCookie: {sessionCookie}\r\nContent-Type: application/json\r\nContent-Length: 33\r\nConnection: close\r\n\r\n{{\"workspaceId\":\"workspace-alpha\"}}"

            Assert.Contains(" 403 ", duplicateOrigin.Split('\n').[0])

            let! forgedOrigin =
                rawRequest
                    server
                    $"POST /api/snapshot HTTP/1.1\r\nHost: {host}\r\nOrigin: http://attacker.invalid\r\nCookie: {sessionCookie}\r\nContent-Type: application/json\r\nContent-Length: 33\r\nConnection: close\r\n\r\n{{\"workspaceId\":\"workspace-alpha\"}}"

            Assert.Contains(" 403 ", forgedOrigin.Split('\n').[0])

            let! invalidWorkspace =
                rawRequest
                    server
                    $"POST /api/snapshot HTTP/1.1\r\nHost: {host}\r\nOrigin: http://{host}\r\nCookie: {sessionCookie}\r\nContent-Type: application/json\r\nContent-Length: 32\r\nConnection: close\r\n\r\n{{\"workspaceId\":\"workspace-beta\"}}"

            Assert.Contains(" 403 ", invalidWorkspace.Split('\n').[0])

            let! duplicateCookie =
                rawRequest
                    server
                    $"GET / HTTP/1.1\r\nHost: {host}\r\nCookie: {sessionCookie}; {sessionCookie}\r\nConnection: close\r\n\r\n"

            Assert.Contains(" 400 ", duplicateCookie.Split('\n').[0])

            let! queryTarget =
                rawRequest
                    server
                    $"GET /?workspaceId=workspace-alpha HTTP/1.1\r\nHost: {host}\r\nCookie: {sessionCookie}\r\nConnection: close\r\n\r\n"

            Assert.Contains(" 400 ", queryTarget.Split('\n').[0])

            let boundedHeaders =
                { immediateOptions () with
                    MaxHeaderBytes = 64 }

            use! boundedServer = start boundedHeaders
            let boundedHost = $"127.0.0.1:{boundedServer.Origin.Port}"

            let! oversizedHeader =
                rawRequest
                    boundedServer
                    ("GET / HTTP/1.1\r\nHost: "
                     + boundedHost
                     + "\r\nX-Fill: "
                     + String('x', 100)
                     + "\r\nConnection: close\r\n\r\n")

            Assert.Contains(" 400 ", oversizedHeader.Split('\n').[0])
        }

    [<Fact>]
    let ``bootstrap and sessions expire and logout revokes`` () =
        task {
            let expiring =
                { immediateOptions () with
                    BootstrapLifetime = TimeSpan.FromMilliseconds 40.0
                    SessionIdleTimeout = TimeSpan.FromMilliseconds 50.0
                    SessionAbsoluteTimeout = TimeSpan.FromMilliseconds 120.0 }

            use! expiredBootstrapServer = start expiring
            do! Task.Delay 80
            let lateHttp, lateHandler = client ()
            use lateHttp = lateHttp
            use lateHandler = lateHandler
            use! expiredBootstrap = lateHttp.GetAsync(expiredBootstrapServer.BootstrapUrl)
            Assert.Equal(HttpStatusCode.NotFound, expiredBootstrap.StatusCode)

            use! server = start expiring
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server
            do! Task.Delay 80
            use! expiredSession = http.GetAsync(Uri(server.Origin, "/"))
            Assert.Equal(HttpStatusCode.Unauthorized, expiredSession.StatusCode)

            let absoluteOptions =
                { immediateOptions () with
                    SessionIdleTimeout = TimeSpan.FromMilliseconds 100.0
                    SessionAbsoluteTimeout = TimeSpan.FromMilliseconds 220.0 }

            use! absoluteServer = start absoluteOptions
            let absoluteHttp, absoluteHandler = client ()
            use absoluteHttp = absoluteHttp
            use absoluteHandler = absoluteHandler
            let! _ = bootstrap absoluteHttp absoluteServer

            for _ in 1..4 do
                do! Task.Delay 40
                use! keptActive = absoluteHttp.GetAsync(Uri(absoluteServer.Origin, "/"))
                Assert.Equal(HttpStatusCode.OK, keptActive.StatusCode)

            do! Task.Delay 80
            use! absoluteExpiry = absoluteHttp.GetAsync(Uri(absoluteServer.Origin, "/"))
            Assert.Equal(HttpStatusCode.Unauthorized, absoluteExpiry.StatusCode)

            use! logoutServer = start (immediateOptions ())
            let logoutHttp, logoutHandler = client ()
            use logoutHttp = logoutHttp
            use logoutHandler = logoutHandler
            let! _ = bootstrap logoutHttp logoutServer

            use logout =
                new HttpRequestMessage(HttpMethod.Post, Uri(logoutServer.Origin, "/api/logout"))

            logout.Headers.Add("Origin", logoutServer.Origin.GetLeftPart(UriPartial.Authority))
            use! logoutResponse = logoutHttp.SendAsync(logout)
            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode)
            use! afterLogout = logoutHttp.GetAsync(Uri(logoutServer.Origin, "/"))
            Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode)
        }

    [<Fact>]
    let ``query slot remains held until timed out provider actually ends`` () =
        task {
            let providerStarted =
                TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

            let providerRelease =
                TaskCompletionSource<Result<byte array, string list>>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                )

            let options =
                { baseOptions (fun _ _ ->
                      providerStarted.TrySetResult() |> ignore
                      providerRelease.Task) with
                    MaxConcurrentQueries = 1
                    SnapshotTimeout = TimeSpan.FromMilliseconds 60.0 }

            use! server = start options
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server
            let origin = Some(server.Origin.GetLeftPart(UriPartial.Authority))

            use first = snapshotRequest server workspaceId origin
            let firstResponseTask = http.SendAsync(first)
            do! providerStarted.Task.WaitAsync(TimeSpan.FromSeconds 2.0)
            use! firstResponse = firstResponseTask
            Assert.Equal(HttpStatusCode.GatewayTimeout, firstResponse.StatusCode)

            use second = snapshotRequest server workspaceId origin
            use! secondResponse = http.SendAsync(second)
            Assert.Equal(enum<HttpStatusCode> 429, secondResponse.StatusCode)

            providerRelease.SetResult(Ok(Encoding.UTF8.GetBytes "{}"))
            do! Task.Delay 30
            use third = snapshotRequest server workspaceId origin
            use! thirdResponse = http.SendAsync(third)
            Assert.Equal(HttpStatusCode.OK, thirdResponse.StatusCode)
        }

    [<Fact>]
    let ``shutdown is bounded while provider ignores cancellation`` () =
        task {
            let providerStarted =
                TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

            let providerRelease =
                TaskCompletionSource<Result<byte array, string list>>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                )

            let options =
                { baseOptions (fun _ _ ->
                      providerStarted.TrySetResult() |> ignore
                      providerRelease.Task) with
                    SnapshotTimeout = TimeSpan.FromSeconds 2.0
                    ShutdownTimeout = TimeSpan.FromMilliseconds 100.0 }

            use! server = start options
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server

            use request =
                snapshotRequest server workspaceId (Some(server.Origin.GetLeftPart(UriPartial.Authority)))

            let responseTask = http.SendAsync(request)
            do! providerStarted.Task.WaitAsync(TimeSpan.FromSeconds 2.0)

            let stopwatch = Stopwatch.StartNew()
            do! server.StopAsync()
            stopwatch.Stop()
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds 1.0)
            do! server.Completion.WaitAsync(TimeSpan.FromSeconds 1.0)
            providerRelease.TrySetResult(Ok(Encoding.UTF8.GetBytes "{}")) |> ignore

            try
                use! response = responseTask
                Assert.True(int response.StatusCode >= 500)
            with :? HttpRequestException ->
                ()
        }

    [<Fact>]
    let ``all served content carries restrictive browser headers`` () =
        task {
            use! server = start (immediateOptions ())
            let http, handler = client ()
            use http = http
            use handler = handler
            let! _ = bootstrap http server
            use! response = http.GetAsync(Uri(server.Origin, "/"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy") |> Seq.exactlyOne)
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options") |> Seq.exactlyOne)
            let csp = response.Headers.GetValues("Content-Security-Policy") |> Seq.exactlyOne
            Assert.Contains("default-src 'self'", csp)
            Assert.Contains("frame-ancestors 'none'", csp)
        }

    [<Fact>]
    let ``Chromium performs real bootstrap snapshot and logout journey`` () =
        task {
            use! server = start (immediateOptions ())

            let projectDirectory =
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."))

            let startInfo = ProcessStartInfo("node")
            startInfo.WorkingDirectory <- projectDirectory
            startInfo.RedirectStandardOutput <- true
            startInfo.RedirectStandardError <- true
            startInfo.ArgumentList.Add("browser-journey.mjs")
            startInfo.ArgumentList.Add(server.BootstrapUrl.AbsoluteUri)
            startInfo.ArgumentList.Add(workspaceId)

            use browserJourney = new Process(StartInfo = startInfo)
            Assert.True(browserJourney.Start())
            let outputTask = browserJourney.StandardOutput.ReadToEndAsync()
            let errorTask = browserJourney.StandardError.ReadToEndAsync()
            do! browserJourney.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds 20.0)
            let! output = outputTask
            let! error = errorTask

            Assert.True(
                browserJourney.ExitCode = 0,
                $"Chromium journey failed with exit {browserJourney.ExitCode}. stdout: {output} stderr: {error}"
            )
        }

    [<Fact>]
    let ``invalid unbounded options fail before binding`` () =
        task {
            let invalid =
                { immediateOptions () with
                    WorkspaceId = ""
                    MaxSessions = 0
                    BindAttempts = 100 }

            let! result = TelemetryDashboardServer.start invalid CancellationToken.None

            match result with
            | Ok server ->
                server.Dispose()
                failwith "invalid options unexpectedly started"
            | Error errors ->
                Assert.Contains(errors, fun error -> error.StartsWith("WorkspaceId", StringComparison.Ordinal))
                Assert.Contains(errors, fun error -> error.StartsWith("MaxSessions", StringComparison.Ordinal))
                Assert.Contains(errors, fun error -> error.StartsWith("BindAttempts", StringComparison.Ordinal))
        }

    [<Fact>]
    let ``caller cancellation stops the listener and pre-cancelled startup is refused`` () =
        task {
            use cancellation = new CancellationTokenSource()
            let! result = TelemetryDashboardServer.start (immediateOptions ()) cancellation.Token

            let server =
                match result with
                | Ok value -> value
                | Error errors -> failwith (String.concat "; " errors)

            use server = server
            cancellation.Cancel()
            do! server.Completion.WaitAsync(TimeSpan.FromSeconds 1.0)

            use alreadyCancelled = new CancellationTokenSource()
            alreadyCancelled.Cancel()
            let! cancelledResult = TelemetryDashboardServer.start (immediateOptions ()) alreadyCancelled.Token

            match cancelledResult with
            | Ok unexpected ->
                unexpected.Dispose()
                failwith "pre-cancelled startup unexpectedly bound"
            | Error errors -> Assert.Contains("Dashboard server startup was cancelled.", errors)
        }
