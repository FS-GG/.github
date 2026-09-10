namespace FS.GG.Telemetry.Browser.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Security
open System.Diagnostics
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Threading
open System.Threading.Tasks
open FS.GG.Telemetry.Host
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Xunit

type private SlowJsonContent(payload:string,started:ManualResetEventSlim,release:ManualResetEventSlim) =
    inherit HttpContent()
    do base.Headers.ContentType<-Headers.MediaTypeHeaderValue("application/json")
    override _.SerializeToStreamAsync(stream,_) = task {
        let bytes=Encoding.UTF8.GetBytes payload
        do! stream.WriteAsync(bytes,0,1)
        do! stream.FlushAsync()
        started.Set()
        do! Task.Run(fun()->release.Wait())
        do! stream.WriteAsync(bytes,1,bytes.Length-1) }
    override _.TryComputeLength(length:byref<int64>) = length <- -1L;false

module BrowserTests =
    let private directory () =
        let path=Path.Combine(Path.GetTempPath(),"browser-tests-"+Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory path |> ignore
        File.SetUnixFileMode(path,UnixFileMode.UserRead|||UnixFileMode.UserWrite|||UnixFileMode.UserExecute)
        path
    let private key () =
        let bytes=RandomNumberGenerator.GetBytes 32
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_'),Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
    let private principal root id workspaces revoked =
        let access,digest=key()
        let path=Path.Combine(root,id+".json")
        File.WriteAllText(path,$"{{\"schema\":\"fsgg.telemetry.browser-key/1\",\"algorithm\":\"sha256\",\"keyHash\":\"{digest}\"}}")
        File.SetUnixFileMode(path,UnixFileMode.UserRead|||UnixFileMode.UserWrite)
        access,{PrincipalId=id;KeyHashFile=path;WorkspaceIds=workspaces;Revoked=revoked}
    let private options origin sessions rate queries =
        {PublicOrigin=Uri origin;IdleLifetime=TimeSpan.FromMinutes 15.;AbsoluteLifetime=TimeSpan.FromHours 8.;MaximumSessions=sessions;LoginAttemptsPerMinute=rate;LoginAdmission=4;QueryAdmission=queries;QueryTimeout=TimeSpan.FromSeconds 1.}

    [<Fact>]
    let ``sessions rotate logout expire and reject producer material`` () =
        let root=directory()
        try
            let access,config=principal root "reader" [|"workspace-a"|] false
            use service=new BrowserSecurity.Service(options "https://localhost:9443" 4 8 2,[|config|])
            Assert.Equal(LoginDenied,service.Login("reader","producer-bearer-token",DateTimeOffset.UnixEpoch))
            let session=match service.Login("reader",access,DateTimeOffset.UnixEpoch) with LoginAccepted(value,_)->value|result->failwithf "%A" result
            let replacement,_=service.Rotate(session,DateTimeOffset.UnixEpoch.AddMinutes 1.) |> Option.get
            Assert.True(service.Validate(session,DateTimeOffset.UnixEpoch.AddMinutes 1.).IsNone)
            Assert.True(service.Validate(replacement,DateTimeOffset.UnixEpoch.AddMinutes 10.).IsSome)
            service.Logout replacement
            Assert.True(service.Validate(replacement,DateTimeOffset.UnixEpoch.AddMinutes 10.).IsNone)
            let expired=match service.Login("reader",access,DateTimeOffset.UnixEpoch) with LoginAccepted(value,_)->value|result->failwithf "%A" result
            Assert.True(service.Validate(expired,DateTimeOffset.UnixEpoch.AddMinutes 16.).IsNone)
            Assert.Equal(0,service.SessionCount)
            match service.Login("reader",access,DateTimeOffset.UnixEpoch.AddMinutes 16.) with LoginAccepted _->()|result->failwithf "expired slot was not reusable: %A" result
            let absolute=match service.Login("reader",access,DateTimeOffset.UnixEpoch.AddMinutes 17.) with LoginAccepted(value,_)->value|result->failwithf "%A" result
            for minute in 27..10..477 do Assert.True(service.Validate(absolute,DateTimeOffset.UnixEpoch.AddMinutes(float minute)).IsSome)
            Assert.True(service.Validate(absolute,DateTimeOffset.UnixEpoch.AddMinutes 498.).IsNone)
            for cycle in 1..100 do
                let created=DateTimeOffset.UnixEpoch.AddDays(float cycle)
                let value=match service.Login("reader",access,created) with LoginAccepted(id,_)->id|result->failwithf "%A" result
                let rotated,_=service.Rotate(value,created.AddMinutes 1.) |> Option.get
                Assert.True(service.Validate(rotated,created.AddMinutes 17.).IsNone)
            Assert.Equal(0,service.AliasCount)
        finally Directory.Delete(root,true)

    [<Fact>]
    let ``session rotation is single winner under concurrency`` () =
        let root=directory()
        try
            let access,config=principal root "reader" [|"workspace-a"|] false
            use service=new BrowserSecurity.Service(options "https://localhost:9443" 8 16 1,[|config|])
            let session=match service.Login("reader",access,DateTimeOffset.UnixEpoch) with LoginAccepted(value,_)->value|result->failwithf "%A" result
            let results=Array.init 16 (fun _->Task.Run(fun()->service.Rotate(session,DateTimeOffset.UnixEpoch.AddMinutes 1.))) |> Task.WhenAll |> fun task->task.Result
            Assert.Equal(1,results|>Array.choose id|>Array.length)
            Assert.Equal(1,service.SessionCount)
            service.Logout session
            Assert.Equal(0,service.SessionCount)
        finally Directory.Delete(root,true)

    [<Fact>]
    let ``capacity rate revoked and unsafe key files fail closed`` () =
        let root=directory()
        try
            let access,config=principal root "reader" [|"workspace-a"|] false
            use capped=new BrowserSecurity.Service(options "https://localhost:9443" 1 8 1,[|config|])
            match capped.Login("reader",access,DateTimeOffset.UnixEpoch) with LoginAccepted _->()|result->failwithf "%A" result
            Assert.Equal(LoginOverloaded,capped.Login("reader",access,DateTimeOffset.UnixEpoch))
            use concurrent=new BrowserSecurity.Service(options "https://localhost:9443" 4 32 1,[|config|])
            let admitted=Array.init 32 (fun _->Task.Run(fun()->concurrent.Login("reader",access,DateTimeOffset.UnixEpoch))) |> Task.WhenAll |> fun task->task.Result
            Assert.Equal(4,admitted|>Array.filter(function LoginAccepted _->true|_->false)|>Array.length)
            use rated=new BrowserSecurity.Service(options "https://localhost:9443" 8 1 1,[|config|])
            Assert.Equal(LoginDenied,rated.Login("unknown",access,DateTimeOffset.UnixEpoch))
            Assert.Equal(LoginOverloaded,rated.Login("reader",access,DateTimeOffset.UnixEpoch))
            let revokedAccess,revoked=principal root "revoked" [|"workspace-a"|] true
            use revokedService=new BrowserSecurity.Service(options "https://localhost:9443" 2 4 1,[|revoked|])
            Assert.Equal(LoginDenied,revokedService.Login("revoked",revokedAccess,DateTimeOffset.UnixEpoch))
            File.SetUnixFileMode(config.KeyHashFile,UnixFileMode.UserRead|||UnixFileMode.GroupRead)
            Assert.True(BrowserSecurity.validatePrincipals [|config|] |> Result.isError)
            let zeroPath=Path.Combine(root,"zero.json")
            File.WriteAllText(zeroPath,$"{{\"schema\":\"fsgg.telemetry.browser-key/1\",\"algorithm\":\"sha256\",\"keyHash\":\"{String('0',64)}\"}}")
            File.SetUnixFileMode(zeroPath,UnixFileMode.UserRead|||UnixFileMode.UserWrite)
            use zeroService=new BrowserSecurity.Service(options "https://localhost:9443" 2 4 1,[|{PrincipalId="zero";KeyHashFile=zeroPath;WorkspaceIds=[|"workspace-a"|];Revoked=false}|])
            Assert.Equal(LoginDenied,zeroService.Login("zero","!",DateTimeOffset.UnixEpoch))
            File.WriteAllText(zeroPath,$"{{\"schema\":\"fsgg.telemetry.browser-key/1\",\"algorithm\":\"sha256\",\"keyHash\":\"{String('0',64)}\\n\"}}")
            Assert.True(BrowserSecurity.validatePrincipals [|{PrincipalId="zero";KeyHashFile=zeroPath;WorkspaceIds=[|"workspace-a"|];Revoked=false}|] |> Result.isError)
        finally Directory.Delete(root,true)

    let private certificate () =
        use rsa=RSA.Create 2048
        let request=CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)
        let san=new SubjectAlternativeNameBuilder()
        san.AddDnsName "localhost"
        request.CertificateExtensions.Add(san.Build())
        use value=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1.),DateTimeOffset.UtcNow.AddHours 1.)
        X509CertificateLoader.LoadPkcs12(value.Export X509ContentType.Pkcs12,null)
    let private port () =
        let listener=new Net.Sockets.TcpListener(IPAddress.Loopback,0)
        listener.Start()
        let value=(listener.LocalEndpoint:?>Net.IPEndPoint).Port
        listener.Stop();value
    let private request (method:HttpMethod) (path:string) (body:string option) (origin:string option) (cookie:string option) =
        let value=new HttpRequestMessage(method,path)
        body|>Option.iter(fun content->value.Content<-new StringContent(content,Encoding.UTF8,"application/json"))
        origin|>Option.iter(fun item->value.Headers.Add("Origin",item))
        cookie|>Option.iter(fun item->value.Headers.Add("Cookie",$"__Host-fsgg_session={item}"))
        value
    let private session (response:HttpResponseMessage) = response.Headers.GetValues("Set-Cookie")|>Seq.head|>fun value->(value.Split(';')[0]).Split('=')[1]
    let private wrongHostStatus port (expected:X509Certificate2) = task {
        use tcp=new Net.Sockets.TcpClient()
        do! tcp.ConnectAsync("127.0.0.1",port)
        use tls=new SslStream(tcp.GetStream(),false,fun _ seen _ errors->seen.GetCertHashString()=expected.GetCertHashString() && (errors&&&SslPolicyErrors.RemoteCertificateNameMismatch)=SslPolicyErrors.None)
        do! tls.AuthenticateAsClientAsync "localhost"
        let request=Encoding.ASCII.GetBytes "GET /private/dashboard/ HTTP/1.1\r\nHost: evil.invalid\r\nConnection: close\r\n\r\n"
        do! tls.WriteAsync request
        use reader=new StreamReader(tls,Encoding.ASCII)
        let! line=reader.ReadLineAsync()
        return line }
    let private runRealBrowser origin access (certificate:X509Certificate2) =
        if Environment.GetEnvironmentVariable("FSGG_RUN_REAL_BROWSER")="1" then
            use rsa=certificate.GetRSAPublicKey()
            let spki=Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))
            let start=ProcessStartInfo("npm","run test:browser -- --reporter=line")
            start.WorkingDirectory<-__SOURCE_DIRECTORY__
            start.UseShellExecute<-false
            start.RedirectStandardOutput<-true
            start.RedirectStandardError<-true
            ["FSGG_BROWSER_BASE_URL",origin;"FSGG_BROWSER_PRINCIPAL_ID","reader";"FSGG_BROWSER_ACCESS_KEY",access;"FSGG_BROWSER_KNOWN_WORKSPACE","workspace-a";"FSGG_BROWSER_UNAVAILABLE_WORKSPACE","workspace-b";"FSGG_BROWSER_CERTIFICATE_SPKI",spki;"FSGG_BROWSER_ASSERT_UNKNOWN_COUNTS","1"]
            |> List.iter(fun (name,value)->start.Environment[name]<-value)
            use child=Process.Start start
            let output=child.StandardOutput.ReadToEnd()
            let error=child.StandardError.ReadToEnd()
            Assert.True(child.WaitForExit(30000),"real browser process timed out")
            Assert.True(child.ExitCode=0,$"real browser journey failed\n{output}\n{error}")

    [<Fact>]
    let ``real TLS endpoints enforce independent auth scope origin capacity and private headers`` () = task {
        let root=directory()
        let access,config=principal root "reader" [|"workspace-a"|] false
        let port=port()
        let origin=$"https://localhost:{port}"
        use security=new BrowserSecurity.Service(options origin 8 32 1,[|config|])
        use cert=certificate()
        let builder=WebApplication.CreateBuilder(WebApplicationOptions(Args=[||]))
        builder.WebHost.ConfigureKestrel(fun server->server.ListenLocalhost(port,fun listen->listen.UseHttps(cert)|>ignore)) |> ignore
        let app=builder.Build()
        let entered=new ManualResetEventSlim(false)
        let release=new ManualResetEventSlim(true)
        let provider workspace item =
            if item=Some "hold" then
                entered.Set()
                release.Wait()
            if item=Some "missing" then Error["sensitive /path sqlite failure"]
            elif item=Some "throw" then failwith "sensitive /path exception"
            else Ok(Encoding.UTF8.GetBytes($"{{\"schema\":\"fsgg.telemetry.private-dashboard/1\",\"workspaceId\":\"{workspace}\",\"observedAt\":\"2026-09-10T00:00:00Z\",\"revision\":\"r1\",\"operational\":{{\"pendingBatches\":0,\"appliedReceipts\":0,\"rejectedReceipts\":0,\"consistency\":\"current\"}},\"items\":[{{\"id\":\"unknown-item\",\"state\":{{\"outcome\":\"unknown\",\"population\":\"unknown\"}},\"usage\":{{\"total\":null}},\"runtime\":{{\"terminal\":null,\"admitted\":null}},\"coverage\":{{\"populationCoverage\":\"unknown\",\"ciInventory\":\"unknown\"}}}},{{\"id\":\"zero-item\",\"state\":{{\"outcome\":\"ready\",\"population\":\"complete\"}},\"usage\":{{\"total\":0}},\"runtime\":{{\"terminal\":0,\"admitted\":0}},\"coverage\":{{\"populationCoverage\":\"complete\",\"ciInventory\":\"complete\"}}}}]}}"))
        BrowserEndpoints.map app (options origin 8 32 1) security provider
        do! app.StartAsync()
        try
            use handler=new HttpClientHandler(UseCookies=false,AutomaticDecompression=DecompressionMethods.None)
            handler.ServerCertificateCustomValidationCallback<-fun _ seen _ errors->seen.Thumbprint=cert.Thumbprint && (errors&&&SslPolicyErrors.RemoteCertificateNameMismatch)=SslPolicyErrors.None
            use client=new HttpClient(handler,BaseAddress=Uri origin)
            use assetRequest=request HttpMethod.Get "/private/dashboard/" None None None
            use! asset=client.SendAsync assetRequest
            Assert.Equal(HttpStatusCode.OK,asset.StatusCode);Assert.True(asset.Headers.Contains "Content-Security-Policy")
            let! wrongHost=wrongHostStatus port cert
            Assert.Contains(" 404 ",wrongHost)
            use badOrigin=request HttpMethod.Post "/private/dashboard/login" (Some($"{{\"principalId\":\"reader\",\"accessKey\":\"{access}\"}}")) (Some "https://evil.invalid") None
            use! badOriginResponse=client.SendAsync badOrigin
            Assert.Equal(HttpStatusCode.BadRequest,badOriginResponse.StatusCode)
            use login=request HttpMethod.Post "/private/dashboard/login" (Some($"{{\"principalId\":\"reader\",\"accessKey\":\"{access}\"}}")) (Some origin) None
            use! loginResponse=client.SendAsync login
            Assert.Equal(HttpStatusCode.OK,loginResponse.StatusCode)
            let cookieHeader=loginResponse.Headers.GetValues("Set-Cookie")|>Seq.head
            Assert.Contains("secure",cookieHeader,StringComparison.OrdinalIgnoreCase)
            Assert.Contains("httponly",cookieHeader,StringComparison.OrdinalIgnoreCase)
            Assert.Contains("samesite=strict",cookieHeader,StringComparison.OrdinalIgnoreCase)
            Assert.Contains("path=/",cookieHeader,StringComparison.OrdinalIgnoreCase)
            Assert.Contains("workspace-a",loginResponse.Content.ReadAsStringAsync().Result)
            let cookie=session loginResponse
            use malformed=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "[]") (Some origin) (Some cookie)
            use! malformedResponse=client.SendAsync malformed
            Assert.Equal(HttpStatusCode.BadRequest,malformedResponse.StatusCode)
            use oversized=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some("{\"workspaceId\":\""+String('x',1100)+"\"}")) (Some origin) (Some cookie)
            use! oversizedResponse=client.SendAsync oversized
            Assert.Equal(enum<HttpStatusCode>413,oversizedResponse.StatusCode)
            use producer=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\"}") (Some origin) None
            producer.Headers.Authorization<-Headers.AuthenticationHeaderValue("Bearer","producer-token")
            use! producerResponse=client.SendAsync producer
            Assert.Equal(HttpStatusCode.Unauthorized,producerResponse.StatusCode)
            let denied item workspace = task {
                use message=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some($"{{\"workspaceId\":\"{workspace}\",\"itemId\":\"{item}\"}}")) (Some origin) (Some cookie)
                return! client.SendAsync message }
            use! cross=denied "anything" "workspace-b"
            use! unknown=denied "missing" "workspace-a"
            Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);Assert.Equal(HttpStatusCode.NotFound,unknown.StatusCode)
            Assert.Equal(cross.Content.ReadAsStringAsync().Result,unknown.Content.ReadAsStringAsync().Result)
            use! thrown=denied "throw" "workspace-a"
            Assert.Equal(HttpStatusCode.ServiceUnavailable,thrown.StatusCode);Assert.DoesNotContain("sensitive",thrown.Content.ReadAsStringAsync().Result)
            use gzip=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\"}") (Some origin) (Some cookie)
            gzip.Headers.AcceptEncoding.ParseAdd "gzip"
            use! success=client.SendAsync gzip
            Assert.Equal(HttpStatusCode.OK,success.StatusCode);Assert.Contains("gzip",success.Content.Headers.ContentEncoding);Assert.Equal("no-store",string success.Headers.CacheControl)
            use slowStarted=new ManualResetEventSlim(false)
            use slowRelease=new ManualResetEventSlim(false)
            use slow=request HttpMethod.Post "/private/dashboard/v1/snapshot" None (Some origin) (Some cookie)
            slow.Content<-new SlowJsonContent("{\"workspaceId\":\"workspace-a\"}",slowStarted,slowRelease)
            let slowResponse=client.SendAsync slow
            Assert.True(slowStarted.Wait(TimeSpan.FromSeconds 2.))
            use blocked=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\"}") (Some origin) (Some cookie)
            use! blockedResponse=client.SendAsync blocked
            Assert.Equal(enum<HttpStatusCode>429,blockedResponse.StatusCode)
            slowRelease.Set()
            use! slowFinished=slowResponse
            Assert.Equal(HttpStatusCode.OK,slowFinished.StatusCode)
            release.Reset()
            use hold=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\",\"itemId\":\"hold\"}") (Some origin) (Some cookie)
            let held=client.SendAsync hold
            Assert.True(entered.Wait(TimeSpan.FromSeconds 2.))
            use second=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\"}") (Some origin) (Some cookie)
            use! overloaded=client.SendAsync second
            Assert.Equal(enum<HttpStatusCode>429,overloaded.StatusCode)
            release.Set()
            use! heldResponse=held
            Assert.Equal(HttpStatusCode.OK,heldResponse.StatusCode)
            use refresh=request HttpMethod.Post "/private/dashboard/v1/session/refresh" None (Some origin) (Some cookie)
            use! refreshed=client.SendAsync refresh
            let replacement=session refreshed
            use replay=request HttpMethod.Post "/private/dashboard/v1/snapshot" (Some "{\"workspaceId\":\"workspace-a\"}") (Some origin) (Some cookie)
            use! replayed=client.SendAsync replay
            Assert.Equal(HttpStatusCode.Unauthorized,replayed.StatusCode)
            use logout=request HttpMethod.Post "/private/dashboard/v1/logout" None (Some origin) (Some replacement)
            use! logoutResponse=client.SendAsync logout
            Assert.Equal(HttpStatusCode.NoContent,logoutResponse.StatusCode)
            runRealBrowser origin access cert
        finally
            release.Set()
            app.StopAsync().GetAwaiter().GetResult()
            app.DisposeAsync().AsTask().GetAwaiter().GetResult()
            Directory.Delete(root,true) }
