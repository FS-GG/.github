namespace FS.GG.Telemetry.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Diagnostics
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Data.Sqlite
open System.Text
open System.Threading
open System.Threading.Tasks
open Xunit
open FS.GG.Coord
open FS.GG.Telemetry
open FS.GG.Telemetry.Host
open FS.GG.Coord.Cli

type private Handler(response:unit->HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(_,_) = Task.FromResult(response())

type private DropFirstResponseHandler(inner:HttpMessageHandler) =
    inherit DelegatingHandler(inner)
    let mutable drop=true
    member private this.Forward(request,cancellationToken) = base.SendAsync(request,cancellationToken)
    override this.SendAsync(request,cancellationToken) = task {
        let! (response:HttpResponseMessage)=this.Forward(request,cancellationToken)
        if drop then
            drop<-false
            response.Dispose()
            return raise(HttpRequestException("synthetic lost response"))
        else return response }

type private SlowContent(started:TaskCompletionSource<unit>) =
    inherit HttpContent()
    override _.TryComputeLength(length:byref<int64>) = length<-0L; false
    override _.SerializeToStreamAsync(stream,_) =
        task {
            do! stream.WriteAsync(ReadOnlyMemory<byte>([|byte '{'|])).AsTask()
            do! stream.FlushAsync()
            started.TrySetResult() |> ignore
            do! Task.Delay(30000)
        } :> Task
    override _.SerializeToStreamAsync(stream,_,cancellationToken) =
        task {
            do! stream.WriteAsync(ReadOnlyMemory<byte>([|byte '{'|]),cancellationToken).AsTask()
            do! stream.FlushAsync(cancellationToken)
            started.TrySetResult() |> ignore
            do! Task.Delay(Timeout.Infinite,cancellationToken)
        } :> Task

module RemoteTelemetryTests =
    let scope:TelemetryReceipt.Scope={Workspace="workspace-a";Producer="producer-a";Stream="runtime"}
    let browserSession={IdleSeconds=300;AbsoluteSeconds=3600;MaximumSessions=32;LoginAttemptsPerMinute=16;LoginAdmission=4;QueryAdmission=4;QueryTimeoutSeconds=10}
    let makeEnvelope (who:TelemetryReceipt.Scope) batch revision = Encoding.UTF8.GetBytes $"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{who.Workspace}","producerId":"{who.Producer}","streamId":"{who.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"native-batch","sourceIdentity":"native-source","generation":"g1","cursor":"1","eventCount":1,"events":[{{"kind":"item","identity":"item-a","itemId":"item-a","revision":{revision}}}]}}}}"""
    let envelope batch = makeEnvelope scope batch 0
    let receipt batch status =
        let parsed=TelemetryReceipt.parse(envelope batch) |> Result.defaultWith(fun e->failwithf "%A" e)
        Encoding.UTF8.GetBytes $"""{{"schema":"{RemoteContract.ReceiptSchema}","workspaceId":"{scope.Workspace}","producerId":"{scope.Producer}","streamId":"{scope.Stream}","batchId":"{batch}","digest":"{parsed.Digest}","status":"{status}","code":null}}"""
    let config:RemoteContract.ClientConfig={Endpoint=Uri("https://telemetry.example/");CredentialReference="main"}
    let resolve _ _=Task.FromResult(Some(String('x',32)))

    [<Fact>]
    let ``client accepts only a complete bound receipt`` () = task {
        use client=new HttpClient(new Handler(fun () -> new HttpResponseMessage(HttpStatusCode.Accepted,Content=new ByteArrayContent(receipt "batch-a" "durably-received"))))
        let! result=RemoteClient.submitWithClientForTesting client config resolve scope (envelope "batch-a") CancellationToken.None
        match result with RemoteClient.Acknowledged value -> Assert.Equal("batch-a",value.BatchId) | _ -> Assert.Fail "expected acknowledgement" }

    [<Fact>]
    let ``202 alone and receipt-looking server errors are never acknowledgements`` () = task {
        for status,body in [HttpStatusCode.Accepted,[||];HttpStatusCode.InternalServerError,receipt "batch-a" "durably-received"] do
            use client=new HttpClient(new Handler(fun () -> new HttpResponseMessage(status,Content=new ByteArrayContent(body))))
            let! result=RemoteClient.submitWithClientForTesting client config resolve scope (envelope "batch-a") CancellationToken.None
            match result with RemoteClient.Unacknowledged _ -> () | _ -> Assert.Fail "invalid acknowledgement" }

    [<Fact>]
    let ``client retains closed terminal error identity`` () = task {
        use client=new HttpClient(new Handler(fun () -> new HttpResponseMessage(HttpStatusCode.Conflict,Content=new ByteArrayContent(RemoteContract.writeError "identity-conflict"))))
        let! result=RemoteClient.submitWithClientForTesting client config resolve scope (envelope "batch-a") CancellationToken.None
        Assert.Equal(RemoteClient.Unacknowledged "identity-conflict",result) }

    [<Fact>]
    let ``client refuses redirects and bounds malformed responses to five attempts`` () = task {
        let mutable redirects=0
        use redirectClient=new HttpClient(new Handler(fun () -> redirects<-redirects+1; new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)))
        let! redirected=RemoteClient.submitWithClientForTesting redirectClient config resolve scope (envelope "batch-a") CancellationToken.None
        Assert.Equal(RemoteClient.Unacknowledged "redirect-refused",redirected)
        Assert.Equal(1,redirects)
        for body in [Array.zeroCreate 4097;Encoding.UTF8.GetBytes "not-json"] do
            let mutable attempts=0
            use malformedClient=new HttpClient(new Handler(fun () -> attempts<-attempts+1; new HttpResponseMessage(HttpStatusCode.Accepted,Content=new ByteArrayContent(body))))
            let! malformed=RemoteClient.submitWithClientForTesting malformedClient config resolve scope (envelope "batch-a") CancellationToken.None
            Assert.Equal(RemoteClient.Unacknowledged "receipt-unavailable",malformed)
            Assert.Equal(5,attempts) }

    [<Fact>]
    let ``receipt decoder is closed and validates digest and rejection code`` () =
        Assert.True(RemoteContract.parseReceipt(receipt "batch-a" "applied") |> Result.isOk)
        let original=Encoding.UTF8.GetString(receipt "batch-a" "applied")
        let extra="{\"extra\":true," + original.Substring(1)
        Assert.True(RemoteContract.parseReceipt(Encoding.UTF8.GetBytes extra) |> Result.isError)
        let semantic=Encoding.UTF8.GetString(receipt "batch-a" "rejected").Replace("\"code\":null","\"code\":\"semantic-conflict\"")
        Assert.True(RemoteContract.parseReceipt(Encoding.UTF8.GetBytes semantic) |> Result.isOk)

    [<Fact>]
    let ``client config accepts only an HTTPS origin and bounded reference`` () =
        for uri in ["http://example.test/";"https://u:p@example.test/";"https://example.test/path";"https://example.test/?x=1"] do
            Assert.True(RemoteContract.validateClientConfig {config with Endpoint=Uri uri} |> Result.isError)
        Assert.True(RemoteContract.validateClientConfig config |> Result.isOk)

    [<Fact>]
    let ``credential rotation keeps scope while revocation and cross-scope token reuse fail`` () =
        let token=String('z',32)
        let hash=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes token)
        let entry={Scope=scope;TokenHash=hash;Revoked=false}
        Assert.Equal(Some scope,Runtime.authenticate (Map.ofList ["old",entry;"new",entry]) token)
        Assert.Equal(None,Runtime.authenticate (Map.ofList ["revoked",{entry with Revoked=true}]) token)

    [<Fact>]
    let ``service lock is exclusive and stale path is reusable`` () =
        let path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".lock")
        try
            let first=Runtime.ServiceLock.Acquire(path) |> Result.defaultWith failwith
            match Runtime.ServiceLock.Acquire(path) with Error _->()|Ok second->(second:>IDisposable).Dispose();Assert.Fail "second lock acquired"
            (first :> IDisposable).Dispose()
            use restarted=Runtime.ServiceLock.Acquire(path) |> Result.defaultWith failwith
            Assert.NotNull restarted
        finally if File.Exists path then File.Delete path

    [<Fact>]
    let ``host config rejects duplicate members before deserialization`` () =
        let path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")+".json")
        try
            File.WriteAllText(path,"{\"ListenUrl\":\"https://127.0.0.1:1\",\"ListenUrl\":\"https://127.0.0.1:2\",\"CertificatePath\":\"/x\",\"CertificatePasswordFile\":\"/y\",\"ServiceLockPath\":\"/z\",\"Stores\":[],\"Credentials\":[]}")
            File.SetUnixFileMode(path,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            Assert.True(Configuration.load path |> Result.isError)
        finally if File.Exists path then File.Delete path

    [<Fact>]
    let ``host config rejects multiple or non-origin listeners`` () =
        for listen in ["https://127.0.0.1:5001/;http://127.0.0.1:5002";"https://user@127.0.0.1:5001";"https://127.0.0.1:5001/path"] do
            let candidate={Schema="fsgg.telemetry.host-config/1";ListenUrl=listen;CertificatePath="/missing";CertificatePasswordFile="/missing";ServiceLockPath="/tmp/fsgg.lock";Stores=[||];Credentials=[||];BrowserPrincipals=[||];BrowserSession=browserSession}
            let errors=match Configuration.validate candidate with Error values->values|Ok _->failwith "invalid listener accepted"
            Assert.Contains("listenUrl must be one HTTPS origin",errors)

    [<Fact>]
    let ``host config rejects credential symlinks and public secret permissions`` () =
        let root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory root |> ignore
        try
            let secret=Path.Combine(root,"secret")
            let link=Path.Combine(root,"link")
            File.WriteAllText(secret,String('s',32)); File.SetUnixFileMode(secret,UnixFileMode.UserRead ||| UnixFileMode.GroupRead)
            File.CreateSymbolicLink(link,secret) |> ignore
            let candidate={Schema="fsgg.telemetry.host-config/1";ListenUrl="https://127.0.0.1:1";CertificatePath=secret;CertificatePasswordFile=secret;ServiceLockPath=Path.Combine(root,"lock");Stores=[|{WorkspaceId="workspace-a";Root=Path.Combine(root,"store")}|];Credentials=[|{Reference="producer";SecretFile=link;WorkspaceId="workspace-a";ProducerId="producer-a";StreamId="runtime";Revoked=false}|];BrowserPrincipals=[||];BrowserSession=browserSession}
            let errors=match Configuration.validate candidate with Error values->values|Ok _->failwith "invalid config accepted"
            Assert.Contains<string>(errors,fun e->e.Contains("symbolic link"))
            Assert.Contains<string>(errors,fun e->e.Contains("permissions"))
        finally Directory.Delete(root,true)

    [<Fact>]
    let ``host global census fails closed on inconsistent accepted obligations`` () = task {
        let root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))
        try
            let stores=[|for index in 1..2 -> Path.Combine(root,string index)|]
            for index,path in Array.indexed stores do
                let enrolled={scope with Workspace=$"workspace-{index}";Producer=$"producer-{index}"}
                TelemetryStoreApplication.initialize path TelemetryStore.ApprovedLocalDurable |> Result.defaultWith(fun e->failwithf "%A" e)|>ignore
                TelemetryStoreApplication.enrollReceiptProducer path TelemetryStore.ApprovedLocalDurable enrolled |> Result.defaultWith(fun e->failwithf "%A" e)|>ignore
                use connection=new SqliteConnection($"Data Source={Path.Combine(path,TelemetryStoreApplication.databaseFileName)};Pooling=False")
                connection.Open()
                use transaction=connection.BeginTransaction()
                for batch in 1..1 do
                    use command=connection.CreateCommand()
                    command.Transaction<-transaction
                    command.CommandText<-"INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,'runtime',$d,1,'durably-received');"
                    command.Parameters.AddWithValue("$p",enrolled.Producer)|>ignore;command.Parameters.AddWithValue("$b",string batch)|>ignore;command.Parameters.AddWithValue("$d",String('a',64))|>ignore
                    command.ExecuteNonQuery()|>ignore
                transaction.Commit()
            let config={Schema="fsgg.telemetry.host-config/1";ListenUrl="https://127.0.0.1:1";CertificatePath="/unused";CertificatePasswordFile="/unused";ServiceLockPath="/unused";Stores=[|{WorkspaceId="workspace-0";Root=stores[0]};{WorkspaceId="workspace-1";Root=stores[1]}|];Credentials=[||];BrowserPrincipals=[||];BrowserSession=browserSession}
            use state=new Runtime.HostState(config,fun _->TelemetryStore.ApprovedLocalDurable)
            let incoming={scope with Workspace="workspace-0";Producer="producer-0"}
            let bytes=envelope "overload" |> fun value->Encoding.UTF8.GetString(value).Replace("workspace-a","workspace-0").Replace("producer-a","producer-0") |> Encoding.UTF8.GetBytes
            let! reply=Runtime.submit state incoming bytes CancellationToken.None
            Assert.Equal(503,reply.Status)
            Assert.Equal(Some "storage-unavailable",RemoteContract.parseError reply.Body)
            state.Ready<-true
            state.StartDrain()
            for _ in 1..50 do
                if state.Ready then do! Task.Delay 20
            Assert.False(state.Ready,"a failed drain must withdraw readiness")
        finally if Directory.Exists root then Directory.Delete(root,true) }

    [<Fact(Timeout=30000)>]
    let ``actual host aggregate refuses new global capacity but preserves identity retries`` () = task {
        let root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))
        try
            let roots=[|Path.Combine(root,"a");Path.Combine(root,"b")|]
            let mutable replayScope=scope
            let mutable replayBytes=[||]
            for storeIndex,store in Array.indexed roots do
                TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable |> Result.defaultWith(fun e->failwithf "%A" e)|>ignore
                Directory.CreateDirectory(Path.Combine(store,"receipt-inbox"))|>ignore
                for producerIndex in 0..3 do
                    let enrolled:TelemetryReceipt.Scope={Workspace=$"workspace-{storeIndex}";Producer=$"producer-{storeIndex}-{producerIndex}";Stream="runtime"}
                    TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable enrolled |> Result.defaultWith(fun e->failwithf "%A" e)|>ignore
                use connection=new SqliteConnection($"Data Source={Path.Combine(store,TelemetryStoreApplication.databaseFileName)};Pooling=False")
                connection.Open()
                use transaction=connection.BeginTransaction()
                for producerIndex in 0..3 do
                    let enrolled:TelemetryReceipt.Scope={Workspace=$"workspace-{storeIndex}";Producer=$"producer-{storeIndex}-{producerIndex}";Stream="runtime"}
                    for batchIndex in 0..127 do
                        let batch=$"batch-{batchIndex}"
                        let bytes=makeEnvelope enrolled batch 0
                        let parsed=TelemetryReceipt.parse bytes |> Result.defaultWith(fun e->failwithf "%A" e)
                        File.WriteAllText(Path.Combine(store,"receipt-inbox",TelemetryReceipt.key enrolled.Producer batch+".ready"),parsed.Canonical)
                        use command=connection.CreateCommand()
                        command.Transaction<-transaction
                        command.CommandText<-"INSERT INTO transport_receipts(producer,batch,stream,digest,payload_bytes,state) VALUES($p,$b,$s,$d,$n,'durably-received');"
                        command.Parameters.AddWithValue("$p",enrolled.Producer)|>ignore;command.Parameters.AddWithValue("$b",batch)|>ignore;command.Parameters.AddWithValue("$s",enrolled.Stream)|>ignore;command.Parameters.AddWithValue("$d",parsed.Digest)|>ignore;command.Parameters.AddWithValue("$n",Encoding.UTF8.GetByteCount parsed.Canonical)|>ignore
                        command.ExecuteNonQuery()|>ignore
                        if storeIndex=0 && producerIndex=0 && batchIndex=0 then replayScope<-enrolled;replayBytes<-bytes
                transaction.Commit()
            let config={Schema="fsgg.telemetry.host-config/1";ListenUrl="https://127.0.0.1:1";CertificatePath="/unused";CertificatePasswordFile="/unused";ServiceLockPath="/unused";Stores=[|{WorkspaceId="workspace-0";Root=roots[0]};{WorkspaceId="workspace-1";Root=roots[1]}|];Credentials=[||];BrowserPrincipals=[||];BrowserSession=browserSession}
            use state=new Runtime.HostState(config,fun _->TelemetryStore.ApprovedLocalDurable)
            let newBytes=makeEnvelope replayScope "new-batch" 0
            let! overloaded=Runtime.submit state replayScope newBytes CancellationToken.None
            Assert.Equal(429,overloaded.Status)
            let! replay=Runtime.submit state replayScope replayBytes CancellationToken.None
            Assert.Equal(200,replay.Status)
            let changed=makeEnvelope replayScope "batch-0" 1
            let! conflict=Runtime.submit state replayScope changed CancellationToken.None
            Assert.Equal(409,conflict.Status)
        finally if Directory.Exists root then Directory.Delete(root,true) }

    [<Fact>]
    let ``host global capacity uses aggregate counts and bytes`` () =
        Assert.True(Capacity.admitsNewIdentity 999999L 1023L (64L*1024L*1024L-10L) 10L)
        Assert.False(Capacity.admitsNewIdentity 1000000L 0L 0L 1L)
        Assert.False(Capacity.admitsNewIdentity 0L 1024L 0L 1L)
        Assert.False(Capacity.admitsNewIdentity 0L 0L (64L*1024L*1024L) 1L)

    [<Fact(Timeout=30000)>]
    let ``real TLS receiver preserves scoped receipt across restart and duplicate submit`` () = task {
        let root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"fsgg-h1-test-"+Guid.NewGuid().ToString("N"))
        let ambientEndpoint=Environment.GetEnvironmentVariable("Kestrel__Endpoints__ambient__Url")
        Environment.SetEnvironmentVariable("Kestrel__Endpoints__ambient__Url","http://127.0.0.1:1")
        Directory.CreateDirectory root |> ignore
        try
            let store=Path.Combine(root,"store")
            TelemetryStoreApplication.initialize store TelemetryStore.ApprovedLocalDurable |> Result.defaultWith(fun e->failwithf "%A" e) |> ignore
            TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable scope |> Result.defaultWith(fun e->failwithf "%A" e) |> ignore
            let otherScope={scope with Producer="producer-b"}
            TelemetryStoreApplication.enrollReceiptProducer store TelemetryStore.ApprovedLocalDurable otherScope |> Result.defaultWith(fun e->failwithf "%A" e) |> ignore
            use listener=new TcpListener(IPAddress.Loopback,0)
            listener.Start()
            let port=(listener.LocalEndpoint :?> Net.IPEndPoint).Port
            listener.Stop()
            let password="test-password"
            use rsa=RSA.Create(2048)
            let request=CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)
            request.CertificateExtensions.Add(X509BasicConstraintsExtension(false,false,0,false))
            request.CertificateExtensions.Add(X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,false))
            let san=SubjectAlternativeNameBuilder()
            san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback)
            request.CertificateExtensions.Add(san.Build())
            use certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddDays(1))
            let certificatePath=Path.Combine(root,"server.pfx")
            let passwordPath=Path.Combine(root,"certificate-password")
            let tokenPath=Path.Combine(root,"producer-token")
            let rotatedTokenPath=Path.Combine(root,"producer-token-rotated")
            let revokedTokenPath=Path.Combine(root,"producer-token-revoked")
            let otherTokenPath=Path.Combine(root,"other-token")
            let configPath=Path.Combine(root,"host.json")
            let lockPath=Path.Combine(root,"host.lock")
            File.WriteAllBytes(certificatePath,certificate.Export(X509ContentType.Pfx,password))
            File.WriteAllText(passwordPath,password)
            let token=String('t',48)
            let rotatedToken=String('r',48)
            let revokedToken=String('v',48)
            let otherToken=String('o',48)
            File.WriteAllText(tokenPath,token)
            File.WriteAllText(rotatedTokenPath,rotatedToken)
            File.WriteAllText(revokedTokenPath,revokedToken)
            File.WriteAllText(otherTokenPath,otherToken)
            for path in [certificatePath;passwordPath;tokenPath;rotatedTokenPath;revokedTokenPath;otherTokenPath] do File.SetUnixFileMode(path,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            let json=$"""{{"Schema":"fsgg.telemetry.host-config/1","ListenUrl":"https://127.0.0.1:{port}","CertificatePath":"{certificatePath}","CertificatePasswordFile":"{passwordPath}","ServiceLockPath":"{lockPath}","Stores":[{{"WorkspaceId":"{scope.Workspace}","Root":"{store}"}}],"Credentials":[{{"Reference":"producer-main","SecretFile":"{tokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":false}},{{"Reference":"producer-rotated","SecretFile":"{rotatedTokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":false}},{{"Reference":"producer-revoked","SecretFile":"{revokedTokenPath}","WorkspaceId":"{scope.Workspace}","ProducerId":"{scope.Producer}","StreamId":"{scope.Stream}","Revoked":true}},{{"Reference":"producer-other","SecretFile":"{otherTokenPath}","WorkspaceId":"{otherScope.Workspace}","ProducerId":"{otherScope.Producer}","StreamId":"{otherScope.Stream}","Revoked":false}}],"BrowserPrincipals":[],"BrowserSession":{{"IdleSeconds":300,"AbsoluteSeconds":3600,"MaximumSessions":32,"LoginAttemptsPerMinute":16,"LoginAdmission":4,"QueryAdmission":4,"QueryTimeoutSeconds":10}}}}"""
            File.WriteAllText(configPath,json)
            File.SetUnixFileMode(configPath,UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
            match Configuration.load configPath with Ok _->()|Error errors->Assert.Fail(sprintf "config rejected: %A" errors)
            let loaded=Configuration.load configPath |> Result.defaultWith(fun e->failwithf "%A" e)
            let credentials=Configuration.credentials loaded
            let start () = task {
                let state=new Runtime.HostState(loaded,fun _->TelemetryStore.ApprovedLocalDurable)
                state.Ready<-true; state.StartDrain()
                let builder=Hosting.createBuilder loaded.ListenUrl certificate
                let app=builder.Build()
                Endpoints.configure app state credentials
                do! app.StartAsync()
                return app,state }
            let handler=new HttpClientHandler(AllowAutoRedirect=false)
            handler.ServerCertificateCustomValidationCallback <- fun _ cert _ errors -> errors=Net.Security.SslPolicyErrors.RemoteCertificateChainErrors && cert.GetCertHashString()=certificate.GetCertHashString()
            use client=new HttpClient(handler)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",token)
            let waitReady () = task {
                let mutable ready=false
                for _ in 1..50 do
                    if not ready then
                        try let! response=client.GetAsync($"https://127.0.0.1:{port}/private/health") in ready<-response.StatusCode=HttpStatusCode.OK with _ -> do! Task.Delay 100
                Assert.True(ready,"receiver did not become ready") }
            let! firstApp,firstState=start()
            do! waitReady()
            Assert.Equal<string>([|loaded.ListenUrl|],firstApp.Urls |> Seq.toArray)
            let remoteConfig:RemoteContract.ClientConfig={Endpoint=Uri($"https://127.0.0.1:{port}/");CredentialReference="local-secret-ref"}
            let dropTransport=new HttpClientHandler(AllowAutoRedirect=false)
            dropTransport.ServerCertificateCustomValidationCallback <- handler.ServerCertificateCustomValidationCallback
            use droppedClient=new HttpClient(new DropFirstResponseHandler(dropTransport))
            let! recoveredLostResponse=RemoteClient.submitWithClientForTesting droppedClient remoteConfig (fun _ _->Task.FromResult(Some token)) scope (envelope "batch-lost-response") CancellationToken.None
            match recoveredLostResponse with RemoteClient.Acknowledged value->Assert.Equal("batch-lost-response",value.BatchId)|_->Assert.Fail "same-identity retry did not recover a lost durable response"
            let! accepted=RemoteClient.submitWithClientForTesting client remoteConfig (fun _ _->Task.FromResult(Some token)) scope (envelope "batch-tls") CancellationToken.None
            match accepted with RemoteClient.Acknowledged value->Assert.Equal("batch-tls",value.BatchId)|_->Assert.Fail "client did not verify durable receipt"
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",String('w',48))
            use! denied=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
            Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",token)
            use! invalidLookup=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/bad!")
            Assert.Equal(HttpStatusCode.BadRequest,invalidLookup.StatusCode)
            Assert.Equal(Some "invalid-request",RemoteContract.parseError(invalidLookup.Content.ReadAsByteArrayAsync().Result))
            use! spoofed=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new ByteArrayContent(makeEnvelope otherScope "spoofed-scope" 0))
            Assert.Equal(HttpStatusCode.Forbidden,spoofed.StatusCode)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",otherToken)
            use! crossScope=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
            Assert.NotEqual(HttpStatusCode.OK,crossScope.StatusCode)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",revokedToken)
            use! revoked=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
            Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",rotatedToken)
            use! rotated=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
            Assert.Equal(HttpStatusCode.OK,rotated.StatusCode)
            client.DefaultRequestHeaders.Authorization<-Headers.AuthenticationHeaderValue("Bearer",token)
            use! invalid=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new ByteArrayContent([|0xffuy|]))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode)
            let unsupported=Encoding.UTF8.GetString(envelope "new-version").Replace("envelope/1","envelope/2")
            use! wrongVersion=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new StringContent(unsupported))
            Assert.Equal(HttpStatusCode.BadRequest,wrongVersion.StatusCode)
            Assert.Equal(Some "unsupported-version",RemoteContract.parseError(wrongVersion.Content.ReadAsByteArrayAsync().Result))
            use! oversized=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new ByteArrayContent(Array.zeroCreate 73729))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,oversized.StatusCode)
            use! duplicate=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new ByteArrayContent(envelope "batch-tls"))
            Assert.True(duplicate.StatusCode=HttpStatusCode.OK || duplicate.StatusCode=HttpStatusCode.Accepted)
            let started=TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
            use slowCts=new CancellationTokenSource()
            let slow=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new SlowContent(started),slowCts.Token)
            do! started.Task
            let countAvailable () =
                let mutable count=0
                while firstState.TryAcquireSlot() do count<-count+1
                for _ in 1..count do firstState.ReleaseSlot()
                count
            let mutable available=16
            for _ in 1..20 do
                if available=16 then
                    do! Task.Delay 50
                    available<-countAvailable()
            Assert.Equal(15,available)
            let mutable held=0
            while held<15 && firstState.TryAcquireSlot() do held<-held+1
            Assert.Equal(15,held)
            Assert.False(firstState.TryAcquireSlot(),"slow HTTP body must hold the sixteenth global admission slot")
            for _ in 1..held do firstState.ReleaseSlot()
            for _ in 1..240 do
                if countAvailable()<>16 then do! Task.Delay 50
            Assert.Equal(16,countAvailable())
            slowCts.Cancel()
            try let! response=slow in response.Dispose() with :? OperationCanceledException -> ()
            let abortedStarted=TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
            use abortedCts=new CancellationTokenSource()
            let aborted=client.PostAsync($"https://127.0.0.1:{port}/v1/batches",new SlowContent(abortedStarted),abortedCts.Token)
            do! abortedStarted.Task
            abortedCts.Cancel()
            try let! response=aborted in response.Dispose() with :? OperationCanceledException -> ()
            for _ in 1..50 do
                if countAvailable()<>16 then do! Task.Delay 20
            Assert.Equal(16,countAvailable())
            do! firstApp.StopAsync()
            do! firstApp.DisposeAsync().AsTask()
            (firstState:>IDisposable).Dispose()
            let! secondApp,secondState=start()
            do! waitReady()
            use! lookup=client.GetAsync($"https://127.0.0.1:{port}/v1/receipts/batch-tls")
            Assert.Equal(HttpStatusCode.OK,lookup.StatusCode)
            let! body=lookup.Content.ReadAsByteArrayAsync()
            match RemoteContract.parseReceipt body with Ok value->Assert.Equal("applied",value.Status)|Error e->Assert.Fail e
            do! secondApp.StopAsync()
            do! secondApp.DisposeAsync().AsTask()
            (secondState:>IDisposable).Dispose()
        finally
            Environment.SetEnvironmentVariable("Kestrel__Endpoints__ambient__Url",ambientEndpoint)
            if Directory.Exists root then Directory.Delete(root,true) }
