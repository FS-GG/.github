namespace FS.GG.Telemetry.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Text.Json.Nodes
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Xunit
open FS.GG.Coord
open FS.GG.Coord.Cli
open FS.GG.Telemetry
open FS.GG.Telemetry.Host

[<Collection("Host schema profile")>]
module HostPassiveReceiptTests =
    [<Fact(Timeout = 30000)>]
    let ``passive HTTP receipt admits Claude transcript provenance and refuses unknown provenance`` () =
        task {
            let root = Path.Combine(Path.GetTempPath(), "fsgg-host-claude-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory root |> ignore
            let approved = TelemetryStore.ApprovedLocalDurable
            let unwrap value = value |> Result.defaultWith (fun errors -> failwithf "%A" errors)
            let store = Path.Combine(root, "store")
            let owner: TelemetryReceipt.Scope = { Workspace = "host-claude"; Producer = "owner"; Stream = "observations" }
            TelemetryStoreApplication.initialize store approved |> unwrap |> ignore
            TelemetryStoreApplication.enrollReceiptPrincipal store approved
                { Scope = owner; Role = TelemetryReceipt.Generic; GrantId = Some "owner-grant"; GrantGeneration = Some 1L } |> unwrap |> ignore
            use listener = new TcpListener(IPAddress.Loopback, 0)
            listener.Start()
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            listener.Stop()
            use rsa = RSA.Create(2048)
            let request = CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            let san = SubjectAlternativeNameBuilder()
            san.AddIpAddress(IPAddress.Loopback)
            request.CertificateExtensions.Add(san.Build())
            use certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1))
            let privateFile (name: string) (value: string) =
                let path = Path.Combine(root, name)
                File.WriteAllText(path, value)
                File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
                path
            let secret = "owner" + String('s', 48)
            let config: HostConfig =
                { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = $"https://127.0.0.1:{port}"
                  CertificatePath = privateFile "unused.pfx" "not loaded by endpoint fixture"
                  CertificatePasswordFile = privateFile "unused.pass" "not loaded by endpoint fixture"
                  ServiceLockPath = Path.Combine(root, "host.lock")
                  Stores = [| { WorkspaceId = owner.Workspace; Root = store } |]
                  Credentials =
                    [| { Reference = owner.Producer; SecretFile = privateFile "owner.key" secret
                         WorkspaceId = owner.Workspace; ProducerId = owner.Producer; StreamId = owner.Stream
                         Role = "generic"; GrantId = "owner-grant"; GrantGeneration = 1L; Revoked = false } |]
                  BrowserPrincipals = [||]; BrowserSession = RemoteTelemetryTests.browserSession }
            let state = new Runtime.HostState(config, fun _ -> approved)
            state.Ready <- true
            state.StartDrain()
            let app = (Hosting.createBuilder config.ListenUrl certificate).Build()
            Endpoints.configure app state (Configuration.credentials config)
            try
                do! app.StartAsync()
                use handler = new HttpClientHandler(AllowAutoRedirect = false)
                // Exact self-signed leaf is trusted only inside this loopback fixture.
                handler.ServerCertificateCustomValidationCallback <- fun _ cert _ errors ->
                    errors = Net.Security.SslPolicyErrors.RemoteCertificateChainErrors
                    && cert.GetCertHashString() = certificate.GetCertHashString()
                use client = new HttpClient(handler)
                let nativeThread = "44444444-4444-4444-4444-444444444444"
                let nativeTurn = "55555555-5555-5555-5555-555555555555"
                let identity (ns: string) =
                    "passive-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ns + "\u001f" + nativeThread + "\u001f" + nativeTurn + "\u001fclaude-response"))).ToLowerInvariant()
                let fact (ns: string) (provenance: string) =
                    JsonNode.Parse($"""{{"kind":"provider-usage-observation/1","identity":"{identity ns}","itemId":null,"revision":1,"threadId":"{nativeThread}","turnId":"{nativeTurn}","responseId":"claude-response","provider":null,"model":null,"effort":null,"input":40,"cachedInput":30,"cacheWriteInput":5,"output":7,"reasoning":null,"total":47,"sourceInstance":"{String('c',64)}","sourceGeneration":"claude-g1","sourceDigest":"{String('d',64)}","position":0,"capturedAt":"2026-10-22T08:00:00Z","occurredAt":null,"provenance":"{provenance}","populationCoverage":"partial","inventoryCoverage":"unknown","terminalCoverage":"unknown","attributionCoverage":"unknown"}}""")
                let send (batch: string) (event: JsonNode) =
                    task {
                        let value = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{owner.Workspace}","producerId":"{owner.Producer}","streamId":"{owner.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{batch}","sourceIdentity":"{owner.Stream}","generation":"host-g1","cursor":"1","eventCount":1,"events":[]}}}}""")
                        value["payload"].["events"].AsArray().Add event
                        use message = new HttpRequestMessage(HttpMethod.Post, config.ListenUrl + "/v1/batches")
                        message.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", secret)
                        message.Content <- new ByteArrayContent(Encoding.UTF8.GetBytes(value.ToJsonString()))
                        use! response = client.SendAsync message
                        let! body = response.Content.ReadAsByteArrayAsync()
                        return response.StatusCode, body
                    }
                // Unknown provenance and a Claude fact under the Codex namespace stay refused at the Host boundary.
                for batch, ns, provenance in [ "unknown-provenance", "claude-transcript/1", "claude-statusline/1"
                                               "foreign-namespace", "codex-rollout/1", "claude-local-transcript/1" ] do
                    let! status, body = send batch (fact ns provenance)
                    Assert.Equal(HttpStatusCode.BadRequest, status)
                    Assert.Equal(Some "invalid-request", RemoteContract.parseError body)
                let! accepted, acceptedBody = send "claude-first" (fact "claude-transcript/1" "claude-local-transcript/1")
                Assert.True(accepted = HttpStatusCode.Accepted || accepted = HttpStatusCode.OK, Encoding.UTF8.GetString acceptedBody)
                let mutable applied = false
                for _ in 1..100 do
                    if not applied then
                        use message = new HttpRequestMessage(HttpMethod.Get, config.ListenUrl + "/v1/receipts/claude-first")
                        message.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", secret)
                        use! response = client.SendAsync message
                        let! bytes = response.Content.ReadAsByteArrayAsync()
                        match RemoteContract.parseReceipt bytes with
                        | Ok value when value.Status = "applied" -> applied <- true
                        | _ -> do! Task.Delay 25
                Assert.True(applied, "Claude transcript receipt never became applied")
                let snapshot = JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved owner |> unwrap)
                let current = Assert.Single(snapshot["observations"].AsArray())
                Assert.Equal(47L, current["total"].GetValue<int64>())
                Assert.Null(current["itemId"])
                use connection = new SqliteConnection($"Data Source={Path.Combine(store, TelemetryStoreApplication.databaseFileName)};Mode=ReadOnly;Pooling=False")
                connection.Open()
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT json_extract(canonical,'$.provenance') || ' ' || identity FROM current_ingest_facts WHERE kind='provider-usage-observation/1';"
                Assert.Equal("claude-local-transcript/1 " + identity "claude-transcript/1", string (command.ExecuteScalar()))
                use items = connection.CreateCommand()
                items.CommandText <- "SELECT count(*) FROM items;"
                Assert.Equal(0L, Convert.ToInt64(items.ExecuteScalar()))
                connection.Close()
            finally
                app.StopAsync().GetAwaiter().GetResult()
                app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                (state :> IDisposable).Dispose()
                if Directory.Exists root then Directory.Delete(root, true)
        }

    [<Fact(Timeout = 30000)>]
    let ``passive HTTP receipt binds generic authority corrections and unknown attribution`` () =
        task {
            let root = Path.Combine(Path.GetTempPath(), "fsgg-host-passive-" + Guid.NewGuid().ToString("N"))
            Directory.CreateDirectory root |> ignore
            let approved = TelemetryStore.ApprovedLocalDurable
            let unwrap value = value |> Result.defaultWith (fun errors -> failwithf "%A" errors)
            let store = Path.Combine(root, "store")
            let owner: TelemetryReceipt.Scope = { Workspace = "host-passive"; Producer = "owner"; Stream = "observations" }
            let peer = { owner with Producer = "peer" }
            let collector = { owner with Producer = "collector" }
            TelemetryStoreApplication.initialize store approved |> unwrap |> ignore
            let principal (scope: TelemetryReceipt.Scope) role : TelemetryReceipt.Principal =
                { Scope = scope; Role = role; GrantId = Some (scope.Producer + "-grant"); GrantGeneration = Some 1L }
            for who, role in [ owner, TelemetryReceipt.Generic; peer, TelemetryReceipt.Generic; collector, TelemetryReceipt.NativeCollector ] do
                TelemetryStoreApplication.enrollReceiptPrincipal store approved (principal who role) |> unwrap |> ignore
            use listener = new TcpListener(IPAddress.Loopback, 0)
            listener.Start()
            let port = (listener.LocalEndpoint :?> IPEndPoint).Port
            listener.Stop()
            use rsa = RSA.Create(2048)
            let request = CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            let san = SubjectAlternativeNameBuilder()
            san.AddIpAddress(IPAddress.Loopback)
            request.CertificateExtensions.Add(san.Build())
            use certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1))
            let privateFile (name: string) (value: string) =
                let path = Path.Combine(root, name)
                File.WriteAllText(path, value)
                File.SetUnixFileMode(path, enum<UnixFileMode> 0o600)
                path
            let token (who: TelemetryReceipt.Scope) = who.Producer + String('s', 48)
            let credential (who: TelemetryReceipt.Scope) (role: string) : CredentialConfig =
                { Reference = who.Producer; SecretFile = privateFile (who.Producer + ".key") (token who)
                  WorkspaceId = who.Workspace; ProducerId = who.Producer; StreamId = who.Stream
                  Role = role; GrantId = who.Producer + "-grant"; GrantGeneration = 1L; Revoked = false }
            let config: HostConfig =
                { Schema = "fsgg.telemetry.host-config/2"; ListenUrl = $"https://127.0.0.1:{port}"
                  CertificatePath = privateFile "unused.pfx" "not loaded by endpoint fixture"
                  CertificatePasswordFile = privateFile "unused.pass" "not loaded by endpoint fixture"
                  ServiceLockPath = Path.Combine(root, "host.lock")
                  Stores = [| { WorkspaceId = owner.Workspace; Root = store } |]
                  Credentials = [| credential owner "generic"; credential peer "generic"; credential collector "native-collector" |]
                  BrowserPrincipals = [||]; BrowserSession = RemoteTelemetryTests.browserSession }
            let state = new Runtime.HostState(config, fun _ -> approved)
            state.Ready <- true
            state.StartDrain()
            let app = (Hosting.createBuilder config.ListenUrl certificate).Build()
            Endpoints.configure app state (Configuration.credentials config)
            let browserAccess = Convert.ToBase64String(Array.create 32 7uy).TrimEnd('=').Replace('+','-').Replace('/','_')
            let browserDigest = Convert.ToHexString(SHA256.HashData(Array.create 32 7uy)).ToLowerInvariant()
            let browserPrincipal: BrowserPrincipalConfig =
                { PrincipalId = "passive-reader"; KeyHashFile = privateFile "browser-key.json" ($"""{{"schema":"fsgg.telemetry.browser-key/1","algorithm":"sha256","keyHash":"{browserDigest}"}}""")
                  WorkspaceIds = [| owner.Workspace |]; Revoked = false }
            let browserOptions: BrowserOptions =
                { PublicOrigin = Uri config.ListenUrl; IdleLifetime = TimeSpan.FromMinutes 5.; AbsoluteLifetime = TimeSpan.FromHours 1.
                  MaximumSessions = 8; LoginAttemptsPerMinute = 32; LoginAdmission = 4; QueryAdmission = 4; QueryTimeout = TimeSpan.FromSeconds 2. }
            use browserSecurity = new BrowserSecurity.Service(browserOptions, [| browserPrincipal |])
            BrowserEndpoints.map app browserOptions browserSecurity (BrowserComposition.snapshotWithAssessment config (fun _ -> approved))
            try
                do! app.StartAsync()
                use handler = new HttpClientHandler(AllowAutoRedirect = false)
                // Exact self-signed leaf is trusted only inside this loopback fixture.
                handler.ServerCertificateCustomValidationCallback <- fun _ cert _ errors ->
                    errors = Net.Security.SslPolicyErrors.RemoteCertificateChainErrors
                    && cert.GetCertHashString() = certificate.GetCertHashString()
                use client = new HttpClient(handler)
                let nativeThread = "22222222-2222-2222-2222-222222222222"
                let nativeTurn = "33333333-3333-3333-3333-333333333333"
                let identity = "passive-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("codex-rollout/1\u001f" + nativeThread + "\u001f" + nativeTurn + "\u001fhost-response"))).ToLowerInvariant()
                let fact (revision: int) =
                    let input = revision * 10
                    JsonNode.Parse($"""{{"kind":"provider-usage-observation/1","identity":"{identity}","itemId":null,"revision":{revision},"threadId":"{nativeThread}","turnId":"{nativeTurn}","responseId":"host-response","provider":null,"model":null,"effort":null,"input":{input},"cachedInput":null,"cacheWriteInput":3,"output":2,"reasoning":null,"total":{input + 2},"sourceInstance":"{String('a',64)}","sourceGeneration":"source-g1","sourceDigest":"{String(char (int 'a' + revision),64)}","position":{revision},"capturedAt":"2026-10-18T12:00:00Z","occurredAt":null,"provenance":"codex-local-rollout/1","populationCoverage":"partial","inventoryCoverage":"unknown","terminalCoverage":"unknown","attributionCoverage":"unknown"}}""")
                let envelope (who: TelemetryReceipt.Scope) (batch: string) (event: JsonNode) =
                    let value = JsonNode.Parse($"""{{"schema":"fsgg.telemetry.envelope/1","workspaceId":"{who.Workspace}","producerId":"{who.Producer}","streamId":"{who.Stream}","batchId":"{batch}","payload":{{"schema":"{TelemetryStore.BatchSchema}","ingestId":"{batch}","sourceIdentity":"{who.Stream}","generation":"host-g1","cursor":"1","eventCount":1,"events":[]}}}}""")
                    value["payload"].["events"].AsArray().Add event
                    Encoding.UTF8.GetBytes(value.ToJsonString())
                let send (who: TelemetryReceipt.Scope) (bytes: byte array) =
                    task {
                        use message = new HttpRequestMessage(HttpMethod.Post, config.ListenUrl + "/v1/batches")
                        message.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token who)
                        message.Content <- new ByteArrayContent(bytes)
                        use! response = client.SendAsync message
                        let! body = response.Content.ReadAsByteArrayAsync()
                        return response.StatusCode, body
                    }
                let receipt (who: TelemetryReceipt.Scope) (batch: string) (expected: string) =
                    task {
                        let mutable found: RemoteContract.Receipt option = None
                        for _ in 1..100 do
                            if found.IsNone then
                                use message = new HttpRequestMessage(HttpMethod.Get, config.ListenUrl + "/v1/receipts/" + batch)
                                message.Headers.Authorization <- Headers.AuthenticationHeaderValue("Bearer", token who)
                                use! response = client.SendAsync message
                                let! bytes = response.Content.ReadAsByteArrayAsync()
                                match RemoteContract.parseReceipt bytes with
                                | Ok value when value.Status = expected -> found <- Some value
                                | _ -> do! Task.Delay 25
                        Assert.True(found.IsSome, $"receipt {batch} never became {expected}")
                        return found.Value
                    }
                let invalidCounters = fact 1
                invalidCounters["total"] <- JsonValue.Create 999L
                let! malformed, malformedBody = send owner (envelope owner "invalid-passive-counters" invalidCounters)
                Assert.Equal(HttpStatusCode.BadRequest, malformed)
                Assert.Equal(Some "invalid-request", RemoteContract.parseError malformedBody)
                let first = envelope owner "passive-first" (fact 1)
                let! accepted, acceptedBody = send owner first
                Assert.True(accepted = HttpStatusCode.Accepted || accepted = HttpStatusCode.OK, Encoding.UTF8.GetString acceptedBody)
                let! initial = receipt owner "passive-first" "applied"
                let! duplicate, duplicateBody = send owner first
                Assert.Equal(HttpStatusCode.OK, duplicate)
                let duplicateReceipt = RemoteContract.parseReceipt duplicateBody |> Result.defaultWith failwith
                Assert.Equal(initial, duplicateReceipt)
                let! corrected, correctedBody = send owner (envelope owner "passive-corrected" (fact 2))
                Assert.True(corrected = HttpStatusCode.Accepted || corrected = HttpStatusCode.OK, Encoding.UTF8.GetString correctedBody)
                let! _ = receipt owner "passive-corrected" "applied"
                let! denied, _ = send peer (envelope owner "wrong-scope" (fact 3))
                Assert.Equal(HttpStatusCode.Forbidden, denied)
                for who, batch, expected in [ peer, "foreign-owner", "semantic-conflict"; collector, "collector-role", "semantic-conflict" ] do
                    let! status, body = send who (envelope who batch (fact 3))
                    Assert.True(status = HttpStatusCode.Accepted || status = HttpStatusCode.OK, Encoding.UTF8.GetString body)
                    let! refused = receipt who batch "rejected"
                    Assert.Equal(Some expected, refused.Code)
                let replacement = JsonNode.Parse($"""{{"kind":"item","identity":"{identity}","itemId":"{identity}","revision":3}}""")
                let! replaced, replacementBody = send owner (envelope owner "wrong-kind" replacement)
                Assert.True(replaced = HttpStatusCode.Accepted || replaced = HttpStatusCode.OK, Encoding.UTF8.GetString replacementBody)
                let! refusedKind = receipt owner "wrong-kind" "rejected"
                Assert.Equal(Some "semantic-conflict", refusedKind.Code)
                let snapshot = JsonNode.Parse(TelemetryStoreApplication.passiveObservations store approved owner |> unwrap)
                let current = Assert.Single(snapshot["observations"].AsArray())
                Assert.Equal(2L, current["revision"].GetValue<int64>())
                Assert.Equal(22L, current["total"].GetValue<int64>())
                Assert.Null(current["cachedInput"])
                Assert.Equal(3L, current["cacheWriteInput"].GetValue<int64>())
                Assert.Null(current["itemId"])
                Assert.Null(current["model"])
                Assert.False(current.AsObject().ContainsKey "cost")
                Assert.Equal("partial", snapshot["populationCoverage"].GetValue<string>())
                let browserRequest route body session =
                    task {
                        use message = new HttpRequestMessage(HttpMethod.Post, config.ListenUrl + route)
                        message.Headers.Add("Origin", config.ListenUrl)
                        session |> Option.iter (fun value -> message.Headers.Add("Cookie", "__Host-fsgg_session=" + value))
                        message.Content <- new StringContent(body, Encoding.UTF8, "application/json")
                        use! response = client.SendAsync message
                        let! bytes = response.Content.ReadAsByteArrayAsync()
                        let cookie = if response.Headers.Contains("Set-Cookie") then response.Headers.GetValues("Set-Cookie") |> Seq.head |> Some else None
                        return response.StatusCode, bytes, cookie
                    }
                let! anonymous, _, _ = browserRequest "/private/dashboard/v1/snapshot" ($"""{{"workspaceId":"{owner.Workspace}"}}""") None
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous)
                let! loggedIn, _, cookie = browserRequest "/private/dashboard/login" ($"""{{"principalId":"passive-reader","accessKey":"{browserAccess}"}}""") None
                Assert.Equal(HttpStatusCode.OK, loggedIn)
                let session = cookie.Value.Split(';').[0].Substring("__Host-fsgg_session=".Length)
                let! displayed, bytes, _ = browserRequest "/private/dashboard/v1/snapshot" ($"""{{"workspaceId":"{owner.Workspace}"}}""") (Some session)
                Assert.Equal(HttpStatusCode.OK, displayed)
                let projection = JsonNode.Parse bytes
                Assert.Equal("fsgg.telemetry.private-dashboard/3", projection["schema"].GetValue<string>())
                let passive = Assert.Single(projection["unassignedUsage"].["observations"].AsArray())
                Assert.Equal("2", passive["revision"].GetValue<string>())
                Assert.Equal("22", passive["total"].GetValue<string>())
                Assert.Null(passive["cachedInput"])
                Assert.Equal(0, projection["items"].AsArray().Count)
                let text = Encoding.UTF8.GetString bytes
                for privateValue in [nativeThread; nativeTurn; "host-response"; identity; "sourceInstance"; "sourceDigest"] do Assert.DoesNotContain(privateValue, text)
                let! outside, outsideBytes, _ = browserRequest "/private/dashboard/v1/snapshot" """{"workspaceId":"outside-workspace"}""" (Some session)
                Assert.Equal(HttpStatusCode.NotFound, outside)
                Assert.DoesNotContain("22", Encoding.UTF8.GetString outsideBytes)
                Assert.True(BrowserComposition.snapshotWithAssessment config (fun _ -> approved) "outside-workspace" None |> Result.isError)
                use connection = new SqliteConnection($"Data Source={Path.Combine(store, TelemetryStoreApplication.databaseFileName)};Mode=ReadOnly;Pooling=False")
                connection.Open()
                let count (sql: string) =
                    use command = connection.CreateCommand()
                    command.CommandText <- sql
                    Convert.ToInt64(command.ExecuteScalar())
                Assert.Equal(0L, count "SELECT count(*) FROM usage_observations;")
                Assert.Equal(0L, count "SELECT count(*) FROM items;")
                Assert.Equal(1L, count "SELECT count(*) FROM corrections WHERE kind='provider-usage-observation/1';")
                connection.Close()
            finally
                app.StopAsync().GetAwaiter().GetResult()
                app.DisposeAsync().AsTask().GetAwaiter().GetResult()
                (state :> IDisposable).Dispose()
                if Directory.Exists root then Directory.Delete(root, true)
        }
